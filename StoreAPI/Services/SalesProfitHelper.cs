using Microsoft.EntityFrameworkCore;
using StoreAPI.Data;
using StoreShared.Sales;

namespace StoreAPI.Services;

internal static class SalesProfitHelper
{
    public static async Task<(decimal CostSold, decimal CostRefunded)> GetCostTotalsAsync(
        StoreDbContext db,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int? sellerUserId,
        CancellationToken ct)
    {
        var fullRefund = nameof(SaleEventType.FullRefund);
        var partialRefund = nameof(SaleEventType.PartialRefund);

        var salesQuery = db.Sales.AsNoTracking()
            .Where(s => s.Timestamp >= fromUtc && s.Timestamp <= toUtc);
        if (sellerUserId is not null)
            salesQuery = salesQuery.Where(s => s.UserId == sellerUserId.Value);

        var saleIds = await salesQuery.Select(s => s.Id).ToListAsync(ct);
        var costSold = saleIds.Count == 0
            ? 0m
            : await db.SaleItems.AsNoTracking()
                .Where(si => saleIds.Contains(si.SaleId))
                .SumAsync(si => si.UnitCost * si.Quantity, ct);

        var refundEventsQuery = db.SaleEvents.AsNoTracking()
            .Where(e => e.Timestamp >= fromUtc && e.Timestamp <= toUtc
                        && (e.EventType == fullRefund || e.EventType == partialRefund));
        if (sellerUserId is not null)
        {
            var sellerSaleIds = db.Sales.AsNoTracking()
                .Where(s => s.UserId == sellerUserId.Value)
                .Select(s => s.Id);
            refundEventsQuery = refundEventsQuery.Where(e => sellerSaleIds.Contains(e.SaleId));
        }

        var refundLines = await refundEventsQuery
            .SelectMany(e => e.Lines)
            .Select(l => new { l.Quantity, l.UnitCost })
            .ToListAsync(ct);

        var costRefunded = refundLines.Sum(l => l.UnitCost * l.Quantity);
        return (costSold, costRefunded);
    }

    public static decimal NetProfit(decimal netRevenue, decimal costSold, decimal costRefunded) =>
        netRevenue - (costSold - costRefunded);
}
