using Microsoft.EntityFrameworkCore;
using StoreAPI.Data;
using StoreShared;
using StoreShared.Sales;

namespace StoreAPI.Services;

public sealed class SalesAnalyticsService
{
    private readonly StoreDbContext _db;

    public SalesAnalyticsService(StoreDbContext db) => _db = db;

    public static (DateTimeOffset From, DateTimeOffset To) RangeUtc(DateOnly from, DateOnly to)
    {
        var fromUtc = new DateTimeOffset(from.Year, from.Month, from.Day, 0, 0, 0, TimeSpan.Zero);
        var toUtc = new DateTimeOffset(to.Year, to.Month, to.Day, 23, 59, 59, 999, TimeSpan.Zero);
        return (fromUtc, toUtc);
    }

    public async Task<SalesStatisticsDto> GetStatisticsAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = RangeUtc(from, to);
        var fullRefund = nameof(SaleEventType.FullRefund);
        var partialRefund = nameof(SaleEventType.PartialRefund);

        var sales = await _db.Sales.AsNoTracking()
            .Where(s => s.Timestamp >= fromUtc && s.Timestamp <= toUtc)
            .Include(s => s.User)
            .Include(s => s.Items)
            .ToListAsync(ct);

        var saleIds = sales.Select(s => s.Id).ToList();

        var saleItems = saleIds.Count == 0
            ? new List<SaleItem>()
            : await _db.SaleItems.AsNoTracking()
                .Where(si => saleIds.Contains(si.SaleId))
                .ToListAsync(ct);

        var skuIds = saleItems.Select(si => si.SKUId).Distinct().ToList();
        var skus = skuIds.Count == 0
            ? new List<SKU>()
            : await _db.Skus.AsNoTracking()
                .Include(s => s.ProductModel)
                .Where(s => skuIds.Contains(s.Id))
                .ToListAsync(ct);

        var skuMap = skus.ToDictionary(s => s.Id);

        var refundEvents = await _db.SaleEvents.AsNoTracking()
            .Where(e => e.Timestamp >= fromUtc && e.Timestamp <= toUtc
                        && (e.EventType == fullRefund || e.EventType == partialRefund))
            .Include(e => e.Lines)
            .ToListAsync(ct);

        var totalSales = sales.Count;
        var totalRevenue = sales.Sum(s => s.TotalAmount);
        var totalItems = saleItems.Sum(si => si.Quantity);
        var avg = totalSales == 0 ? 0 : totalRevenue / totalSales;

        var refundCount = refundEvents.Count;
        var refundAmount = refundEvents.Sum(e => e.AmountAffected ?? 0);
        var refundRate = totalSales == 0 ? 0 : (decimal)refundCount / totalSales * 100m;

        var topRefundProduct = refundEvents
            .SelectMany(e => e.Lines)
            .GroupBy(l => l.ProductName)
            .Select(g => new { Name = g.Key, Count = g.Sum(x => x.Quantity) })
            .OrderByDescending(x => x.Count)
            .FirstOrDefault();

        var salesPerCashier = sales
            .GroupBy(s => s.User?.Username ?? "—")
            .Select(g => new CashierMetricDto(g.Key, g.Count(), g.Sum(x => x.TotalAmount)))
            .OrderByDescending(x => x.Amount)
            .ToList();

        var refundsPerCashier = refundEvents
            .GroupBy(e => e.PerformedBy)
            .Select(g => new CashierMetricDto(g.Key, g.Count(), g.Sum(x => x.AmountAffected ?? 0)))
            .OrderByDescending(x => x.Count)
            .ToList();

        var salesPerDay = sales
            .GroupBy(s => DateOnly.FromDateTime(s.Timestamp.UtcDateTime))
            .OrderBy(g => g.Key)
            .Select(g => new DailyMetricDto(g.Key, g.Sum(x => x.TotalAmount), g.Count()))
            .ToList();

        var refundsPerDay = refundEvents
            .GroupBy(e => DateOnly.FromDateTime(e.Timestamp.UtcDateTime))
            .OrderBy(g => g.Key)
            .Select(g => new DailyMetricDto(g.Key, g.Sum(x => x.AmountAffected ?? 0), g.Count()))
            .ToList();

        var topSelling = saleItems
            .GroupBy(si => si.SKUId)
            .Select(g =>
            {
                skuMap.TryGetValue(g.Key, out var sku);
                var revenue = g.Sum(x => x.UnitPrice * x.Quantity);
                var cost = g.Sum(x => x.UnitCost * x.Quantity);
                return new
                {
                    g.Key,
                    Name = sku?.ProductModel?.Name ?? "—",
                    Size = sku?.Size.ToString() ?? "",
                    Quantity = g.Sum(x => x.Quantity),
                    Revenue = revenue,
                    Cost = cost,
                    Profit = revenue - cost,
                };
            })
            .OrderByDescending(x => x.Quantity)
            .Take(10)
            .Select(x => new TopSkuMetricDto(x.Key, x.Name, x.Size, x.Quantity, x.Revenue, x.Cost, x.Profit))
            .ToList();

        var topRefunded = refundEvents
            .SelectMany(e => e.Lines)
            .GroupBy(l => new { l.SkuId, l.ProductName, l.Size })
            .Select(g =>
            {
                var revenue = g.Sum(x => x.LineTotal);
                var cost = g.Sum(x => x.UnitCost * x.Quantity);
                return new TopSkuMetricDto(
                    g.Key.SkuId,
                    g.Key.ProductName,
                    g.Key.Size,
                    g.Sum(x => x.Quantity),
                    revenue,
                    cost,
                    revenue - cost);
            })
            .OrderByDescending(x => x.Quantity)
            .Take(10)
            .ToList();

        var costSold = saleItems.Sum(si => si.UnitCost * si.Quantity);
        var costRefunded = refundEvents.SelectMany(e => e.Lines).Sum(l => l.UnitCost * l.Quantity);
        var netRevenue = totalRevenue - refundAmount;
        var netProfit = SalesProfitHelper.NetProfit(netRevenue, costSold, costRefunded);

        var saleIdsByDay = sales
            .GroupBy(s => DateOnly.FromDateTime(s.Timestamp.UtcDateTime))
            .ToDictionary(g => g.Key, g => g.Select(s => s.Id).ToHashSet());

        var refundByDay = refundEvents
            .GroupBy(e => DateOnly.FromDateTime(e.Timestamp.UtcDateTime))
            .ToDictionary(
                g => g.Key,
                g => (
                    Revenue: g.Sum(x => x.AmountAffected ?? 0),
                    Cost: g.SelectMany(x => x.Lines).Sum(l => l.UnitCost * l.Quantity)));

        var allDays = saleIdsByDay.Keys
            .Union(refundByDay.Keys)
            .Union(salesPerDay.Select(d => d.Date))
            .OrderBy(d => d);

        var profitPerDay = allDays.Select(day =>
        {
            saleIdsByDay.TryGetValue(day, out var ids);
            ids ??= [];
            var dayRevenue = sales.Where(s => ids.Contains(s.Id)).Sum(s => s.TotalAmount);
            var dayCostSold = saleItems.Where(si => ids.Contains(si.SaleId)).Sum(si => si.UnitCost * si.Quantity);
            var dayRefundRevenue = 0m;
            var dayRefundCost = 0m;
            if (refundByDay.TryGetValue(day, out var refund))
            {
                dayRefundRevenue = refund.Revenue;
                dayRefundCost = refund.Cost;
            }
            var dayNetRevenue = dayRevenue - dayRefundRevenue;
            var profit = SalesProfitHelper.NetProfit(dayNetRevenue, dayCostSold, dayRefundCost);
            return new DailyMetricDto(day, profit, 0);
        }).ToList();

        return new SalesStatisticsDto(
            from,
            to,
            totalSales,
            totalRevenue,
            avg,
            totalItems,
            refundCount,
            refundAmount,
            refundRate,
            costSold - costRefunded,
            netProfit,
            topRefundProduct?.Name,
            topRefundProduct?.Count ?? 0,
            salesPerCashier,
            refundsPerCashier,
            salesPerDay,
            refundsPerDay,
            profitPerDay,
            topSelling,
            topRefunded);
    }

    public async Task<PagedSaleEventsResult> ListEventsAsync(
        DateOnly? from,
        DateOnly? to,
        SaleEventType? type,
        string? performedBy,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.SaleEvents.AsNoTracking().AsQueryable();

        if (from is not null && to is not null)
        {
            var (fromUtc, toUtc) = RangeUtc(from.Value, to.Value);
            query = query.Where(e => e.Timestamp >= fromUtc && e.Timestamp <= toUtc);
        }

        if (type is not null)
            query = query.Where(e => e.EventType == type.Value.ToString());

        if (!string.IsNullOrWhiteSpace(performedBy))
            query = query.Where(e => e.PerformedBy == performedBy.Trim());

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(e => e.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(e => e.Lines)
            .ToListAsync(ct);

        return new PagedSaleEventsResult(
            items.Select(RefundService.MapEvent).ToList(),
            total,
            page,
            pageSize);
    }
}
