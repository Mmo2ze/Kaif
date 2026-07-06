using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using StoreAPI.Data;
using StoreAPI.Services;
using StoreShared;
using StoreShared.Sales;
using StoreShared.Stock;

namespace StoreAPI.Controllers;

[ApiController]
[Route("api/sales")]
public sealed class SalesController : ControllerBase
{
    private readonly StoreDbContext _db;
    private readonly StockService _stock;
    private readonly RefundService _refunds;
    private readonly SalesAnalyticsService _analytics;
    private readonly IMemoryCache _cache;
    private readonly StoreRuntimeSettings _storeSettings;

    public SalesController(
        StoreDbContext db,
        StockService stock,
        RefundService refunds,
        SalesAnalyticsService analytics,
        IMemoryCache cache,
        StoreRuntimeSettings storeSettings)
    {
        _db = db;
        _stock = stock;
        _refunds = refunds;
        _analytics = analytics;
        _cache = cache;
        _storeSettings = storeSettings;
    }

    /// <summary>Complete a sale: validates stock, decrements inventory, persists sale + lines.</summary>
    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Admin) + "," + nameof(UserRole.Seller))]
    public async Task<ActionResult<SaleCreatedDto>> Create([FromBody] CreateSaleRequest body, CancellationToken ct)
    {
        if (body.Items is null || body.Items.Count == 0)
            return BadRequest("At least one line item is required.");

        if (body.DiscountAmount < 0)
            return BadRequest("Discount cannot be negative.");

        foreach (var line in body.Items)
        {
            if (line.Quantity <= 0)
                return BadRequest("Each line must have quantity greater than zero.");
            if (line.UnitPrice < 0)
                return BadRequest("Unit price cannot be negative.");
        }

        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (idClaim is null || !int.TryParse(idClaim, out var userId))
            return Unauthorized();

        var isSeller = User.IsInRole(nameof(UserRole.Seller));
        if (body.DiscountAmount > 0 && isSeller && !_storeSettings.AllowSellerDiscount)
        {
            if (string.IsNullOrWhiteSpace(body.DiscountAuthorizationToken))
                return BadRequest("Discount requires manager authorization.");
            var cacheKey = $"discount-auth:{body.DiscountAuthorizationToken}";
            if (!_cache.TryGetValue(cacheKey, out int authUserId) || authUserId != userId)
                return BadRequest("Discount authorization invalid or expired.");
            _cache.Remove(cacheKey);
        }

        var subtotal = body.Items.Sum(i => i.UnitPrice * i.Quantity);
        var total = subtotal - body.DiscountAmount;
        if (total < 0)
            return BadRequest("Discount exceeds line total.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var sale = new Sale
            {
                UserId = userId,
                Timestamp = DateTimeOffset.UtcNow,
                TotalAmount = total,
                DiscountAmount = body.DiscountAmount,
            };
            _db.Sales.Add(sale);
            await _db.SaveChangesAsync(ct);
            sale.ReceiptNumber = ReceiptNumberFormat.ForSale(sale.Id);

            var username = User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name ?? "cashier";
            var completedLines = new List<SaleEventLine>();

            foreach (var line in body.Items)
            {
                var sku = await _db.Skus.AsNoTracking()
                    .Include(s => s.ProductModel)
                    .FirstAsync(s => s.Id == line.SkuId, ct);

                _db.SaleItems.Add(new SaleItem
                {
                    SaleId = sale.Id,
                    SKUId = line.SkuId,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    UnitCost = Math.Max(0, CatalogPricing.ForSku(sku).BuyPrice),
                });
                completedLines.Add(new SaleEventLine
                {
                    SkuId = line.SkuId,
                    ProductName = sku.ProductModel?.Name ?? "—",
                    Size = sku.Size.ToString(),
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    LineTotal = line.UnitPrice * line.Quantity,
                });

                var (ok, error) = await _stock.TrySubtractAsync(line.SkuId, line.Quantity, ct);
                if (!ok)
                {
                    await tx.RollbackAsync(ct);
                    return BadRequest(error);
                }
            }

            _db.SaleEvents.Add(new SaleEvent
            {
                SaleId = sale.Id,
                ReceiptNumber = sale.ReceiptNumber,
                EventType = nameof(SaleEventType.Completed),
                Timestamp = sale.Timestamp,
                PerformedBy = username,
                AmountAffected = sale.TotalAmount,
                Lines = completedLines,
            });

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Ok(new SaleCreatedDto(sale.Id, sale.TotalAmount, sale.ReceiptNumber));
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    [HttpGet("summary")]
    [Authorize(Roles = nameof(UserRole.Admin) + "," + nameof(UserRole.Seller))]
    public async Task<IActionResult> Summary(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] int? sellerUserId,
        CancellationToken ct)
    {
        if (!TryResolveSalesQuery(from, to, sellerUserId, out var fromUtc, out var toUtc, out var sellerFilter, out _, out var err))
            return err!;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var t0 = StartOfDayUtc(today);
        var t1 = EndOfDayUtc(today);
        var todayQuery = _db.Sales.AsNoTracking().Where(s => s.Timestamp >= t0 && s.Timestamp <= t1);
        if (sellerFilter is not null)
            todayQuery = todayQuery.Where(s => s.UserId == sellerFilter.Value);
        var todayRevenue = await todayQuery.SumAsync(s => s.TotalAmount, ct);

        var rangeQuery = _db.Sales.AsNoTracking().Where(s => s.Timestamp >= fromUtc && s.Timestamp <= toUtc);
        if (sellerFilter is not null)
            rangeQuery = rangeQuery.Where(s => s.UserId == sellerFilter.Value);

        var rangeCount = await rangeQuery.CountAsync(ct);
        var rangeRevenue = rangeCount == 0 ? 0 : await rangeQuery.SumAsync(s => s.TotalAmount, ct);
        var rangeAvg = rangeCount == 0 ? 0 : rangeRevenue / rangeCount;

        var fullRefund = nameof(SaleEventType.FullRefund);
        var partialRefund = nameof(SaleEventType.PartialRefund);

        var todayRefunded = await SumRefundsInRangeAsync(t0, t1, sellerFilter, fullRefund, partialRefund, ct);
        var rangeRefunded = await SumRefundsInRangeAsync(fromUtc, toUtc, sellerFilter, fullRefund, partialRefund, ct);

        // Avoid OrderBy-after-GroupBy in one query (can hang or mis-translate on SQLite).
        var topAgg = from si in _db.SaleItems.AsNoTracking()
            join s in _db.Sales.AsNoTracking() on si.SaleId equals s.Id
            join sku in _db.Skus.AsNoTracking() on si.SKUId equals sku.Id
            join pm in _db.ProductModels.AsNoTracking() on sku.ProductModelId equals pm.Id
            where s.Timestamp >= fromUtc && s.Timestamp <= toUtc
                  && (!sellerFilter.HasValue || s.UserId == sellerFilter.Value)
            group si by pm.Name into g
            select new { Name = g.Key, Qty = g.Sum(x => x.Quantity) };
        var top = await topAgg
            .OrderByDescending(x => x.Qty)
            .FirstOrDefaultAsync(ct);

        var (rangeCostSold, rangeCostRefunded) = await SalesProfitHelper.GetCostTotalsAsync(
            _db, fromUtc, toUtc, sellerFilter, ct);
        var (todayCostSold, todayCostRefunded) = await SalesProfitHelper.GetCostTotalsAsync(
            _db, t0, t1, sellerFilter, ct);
        var rangeNetRevenue = rangeRevenue - rangeRefunded;
        var todayNetRevenue = todayRevenue - todayRefunded;

        var isSeller = User.IsInRole(nameof(UserRole.Seller));
        var rangeCost = rangeCostSold - rangeCostRefunded;
        var todayCost = todayCostSold - todayCostRefunded;
        var rangeProfit = isSeller
            ? 0
            : SalesProfitHelper.NetProfit(rangeNetRevenue, rangeCostSold, rangeCostRefunded);
        var todayProfit = isSeller
            ? 0
            : SalesProfitHelper.NetProfit(todayNetRevenue, todayCostSold, todayCostRefunded);

        return Ok(new SalesSummaryDto(
            todayRevenue,
            rangeCount,
            rangeAvg,
            top?.Name,
            top?.Qty ?? 0,
            rangeRevenue,
            rangeRefunded,
            rangeNetRevenue,
            todayRefunded,
            todayNetRevenue,
            isSeller ? 0 : rangeCost,
            rangeProfit,
            isSeller ? 0 : todayCost,
            todayProfit));
    }

    private async Task<decimal> SumRefundsInRangeAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int? sellerFilter,
        string fullRefund,
        string partialRefund,
        CancellationToken ct)
    {
        var query = _db.SaleEvents.AsNoTracking()
            .Where(e => e.Timestamp >= fromUtc && e.Timestamp <= toUtc
                        && (e.EventType == fullRefund || e.EventType == partialRefund));

        if (sellerFilter is not null)
        {
            var sellerSaleIds = _db.Sales.AsNoTracking()
                .Where(s => s.UserId == sellerFilter.Value)
                .Select(s => s.Id);
            query = query.Where(e => sellerSaleIds.Contains(e.SaleId));
        }

        return await query.SumAsync(e => e.AmountAffected ?? 0, ct);
    }

    [HttpGet]
    [Authorize(Roles = nameof(UserRole.Admin) + "," + nameof(UserRole.Seller))]
    public async Task<IActionResult> ListHistory(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] int? sellerUserId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!TryResolveSalesQuery(from, to, sellerUserId, out var fromUtc, out var toUtc, out var sellerFilter, out _, out var err))
            return err!;
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var baseQuery = _db.Sales.AsNoTracking()
            .Where(s => s.Timestamp >= fromUtc && s.Timestamp <= toUtc);
        if (sellerFilter is not null)
            baseQuery = baseQuery.Where(s => s.UserId == sellerFilter.Value);

        var total = await baseQuery.CountAsync(ct);
        var pageSales = await baseQuery
            .OrderByDescending(s => s.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(s => s.User)
            .Include(s => s.Items)
            .AsSplitQuery()
            .ToListAsync(ct);

        var saleIds = pageSales.Select(s => s.Id).ToList();
        var refundedBySale = await _db.SaleEvents.AsNoTracking()
            .Where(e => saleIds.Contains(e.SaleId)
                        && (e.EventType == nameof(SaleEventType.FullRefund)
                            || e.EventType == nameof(SaleEventType.PartialRefund)))
            .GroupBy(e => e.SaleId)
            .Select(g => new { SaleId = g.Key, Total = g.Sum(x => x.AmountAffected ?? 0) })
            .ToDictionaryAsync(x => x.SaleId, x => x.Total, ct);

        var items = pageSales
            .Select(s => new SaleHistoryRowDto(
                s.Id,
                string.IsNullOrEmpty(s.ReceiptNumber) ? ReceiptNumberFormat.ForSale(s.Id) : s.ReceiptNumber,
                s.Timestamp,
                s.User?.Username ?? "",
                s.Items.Count,
                s.Items.Sum(li => li.Quantity),
                s.TotalAmount + s.DiscountAmount,
                s.DiscountAmount,
                s.TotalAmount,
                s.IsFullyRefunded,
                refundedBySale.GetValueOrDefault(s.Id)))
            .ToList();

        return Ok(new PagedSalesResult(items, total, page, pageSize));
    }

    [HttpGet("receipt/{receiptNumber}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> GetByReceiptNumber(string receiptNumber, CancellationToken ct)
    {
        var sale = await _refunds.GetSaleByReceiptAsync(receiptNumber, ct);
        if (sale is null)
            return NotFound();
        return Ok(sale);
    }

    [HttpGet("receipt/{receiptNumber}/history")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> GetReceiptHistory(string receiptNumber, CancellationToken ct)
    {
        var history = await _refunds.GetReceiptHistoryAsync(receiptNumber, ct);
        if (history is null)
            return NotFound();
        return Ok(history);
    }

    [HttpGet("events")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> ListEvents(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] SaleEventType? type,
        [FromQuery] string? performedBy,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        DateOnly? fromD = null;
        DateOnly? toD = null;
        if (!string.IsNullOrWhiteSpace(from) && !string.IsNullOrWhiteSpace(to))
        {
            if (!DateOnly.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f)
                || !DateOnly.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
                return BadRequest("Invalid from/to dates.");
            fromD = f;
            toD = t;
        }

        var result = await _analytics.ListEventsAsync(fromD, toD, type, performedBy, page, pageSize, ct);
        return Ok(result);
    }

    [HttpGet("events/export")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> ExportEvents(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] SaleEventType? type,
        [FromQuery] string? performedBy,
        CancellationToken ct = default)
    {
        if (!DateOnly.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fromD)
            || !DateOnly.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.None, out var toD))
            return BadRequest("Query parameters 'from' and 'to' are required (yyyy-MM-dd).");

        var all = await _analytics.ListEventsAsync(fromD, toD, type, performedBy, 1, 10_000, ct);
        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,ReceiptNumber,EventType,RefundReceiptNumber,Amount,PerformedBy,Note,SkuId,Product,Size,Qty,LineTotal");
        foreach (var ev in all.Items)
        {
            if (ev.Lines is null || ev.Lines.Count == 0)
            {
                sb.Append(ev.Timestamp.ToString("O", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(CsvEscape(ev.ReceiptNumber)).Append(',');
                sb.Append(ev.EventType).Append(',');
                sb.Append(CsvEscape(ev.RefundReceiptNumber)).Append(',');
                sb.Append((ev.AmountAffected ?? 0).ToString("F2", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(CsvEscape(ev.PerformedBy)).Append(',');
                sb.Append(CsvEscape(ev.Note)).AppendLine(",,,,,");
                continue;
            }

            foreach (var line in ev.Lines)
            {
                sb.Append(ev.Timestamp.ToString("O", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(CsvEscape(ev.ReceiptNumber)).Append(',');
                sb.Append(ev.EventType).Append(',');
                sb.Append(CsvEscape(ev.RefundReceiptNumber)).Append(',');
                sb.Append((ev.AmountAffected ?? 0).ToString("F2", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(CsvEscape(ev.PerformedBy)).Append(',');
                sb.Append(CsvEscape(ev.Note)).Append(',');
                sb.Append(line.SkuId).Append(',');
                sb.Append(CsvEscape(line.ProductName)).Append(',');
                sb.Append(CsvEscape(line.Size)).Append(',');
                sb.Append(line.Quantity).Append(',');
                sb.AppendLine(line.LineTotal.ToString("F2", CultureInfo.InvariantCulture));
            }
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"sale-events-{fromD:yyyyMMdd}-{toD:yyyyMMdd}.csv");
    }

    [HttpGet("statistics")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Statistics(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct = default)
    {
        if (!DateOnly.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fromD)
            || !DateOnly.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.None, out var toD))
            return BadRequest("Query parameters 'from' and 'to' are required (yyyy-MM-dd).");

        if (fromD > toD)
            return BadRequest("'from' cannot be after 'to'.");

        var stats = await _analytics.GetStatisticsAsync(fromD, toD, ct);
        return Ok(stats);
    }

    [HttpGet("{id:int}")]
    [Authorize(Roles = nameof(UserRole.Admin) + "," + nameof(UserRole.Seller))]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (idClaim is null || !int.TryParse(idClaim, out var claimUserId))
            return Unauthorized();

        var sale = await _db.Sales.AsNoTracking()
            .Include(s => s.User)
            .Include(s => s.Items).ThenInclude(i => i.SKU!).ThenInclude(sku => sku.ProductModel)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sale is null)
            return NotFound();

        if (User.IsInRole(nameof(UserRole.Seller)) && sale.UserId != claimUserId)
            return Forbid();

        var totalRefunded = await _refunds.GetTotalRefundedAsync(sale.Id, ct);
        var row = new SaleHistoryRowDto(
            sale.Id,
            string.IsNullOrEmpty(sale.ReceiptNumber) ? ReceiptNumberFormat.ForSale(sale.Id) : sale.ReceiptNumber,
            sale.Timestamp,
            sale.User?.Username ?? "",
            sale.Items.Count,
            sale.Items.Sum(i => i.Quantity),
            sale.TotalAmount + sale.DiscountAmount,
            sale.DiscountAmount,
            sale.TotalAmount,
            sale.IsFullyRefunded,
            totalRefunded);

        var lines = sale.Items
            .OrderBy(i => i.Id)
            .Select(i => new SaleLineDetailDto(
                i.SKU?.ProductModel?.Name ?? "—",
                i.SKU?.Size ?? default,
                i.SKU?.Barcode ?? "",
                i.Quantity,
                i.UnitPrice,
                i.UnitPrice * i.Quantity))
            .ToList();

        return Ok(new SaleHistoryDetailDto(row, lines));
    }

    [HttpGet("export")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Export([FromQuery] string? from, [FromQuery] string? to, [FromQuery] int? sellerUserId, CancellationToken ct)
    {
        if (!TryResolveSalesQuery(from, to, sellerUserId, out var fromUtc, out var toUtc, out var sellerFilter, out _, out var err))
            return err!;

        var sales = await _db.Sales.AsNoTracking()
            .Where(s => s.Timestamp >= fromUtc && s.Timestamp <= toUtc)
            .Where(s => !sellerFilter.HasValue || s.UserId == sellerFilter.Value)
            .OrderBy(s => s.Timestamp)
            .Include(s => s.User)
            .Include(s => s.Items).ThenInclude(i => i.SKU!).ThenInclude(sku => sku.ProductModel)
            .ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("SaleId,Timestamp,Cashier,ProductModel,Size,Barcode,Quantity,UnitPrice,LineTotal,SaleDiscount,SaleTotal");
        foreach (var sale in sales)
        {
            var cashier = CsvEscape(sale.User?.Username);
            foreach (var i in sale.Items.OrderBy(x => x.Id))
            {
                var model = CsvEscape(i.SKU?.ProductModel?.Name);
                var size = i.SKU?.Size.ToString() ?? "";
                var barcode = CsvEscape(i.SKU?.Barcode);
                var lineTotal = i.UnitPrice * i.Quantity;
                sb.Append(sale.Id.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(sale.Timestamp.ToString("O", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(cashier).Append(',');
                sb.Append(model).Append(',');
                sb.Append(CsvEscape(size)).Append(',').Append(barcode).Append(',');
                sb.Append(i.Quantity.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(i.UnitPrice.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(lineTotal.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(sale.DiscountAmount.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
                sb.AppendLine(sale.TotalAmount.ToString("F2", CultureInfo.InvariantCulture));
            }
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        var fname = $"sales-export-{fromUtc:yyyyMMdd}-{toUtc:yyyyMMdd}.csv";
        return File(bytes, "text/csv; charset=utf-8", fname);
    }

    private static DateTimeOffset StartOfDayUtc(DateOnly d) =>
        new(d.Year, d.Month, d.Day, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset EndOfDayUtc(DateOnly d) =>
        new(d.Year, d.Month, d.Day, 23, 59, 59, 999, TimeSpan.Zero);

    private bool TryResolveSalesQuery(
        string? fromRaw,
        string? toRaw,
        int? sellerUserIdQuery,
        out DateTimeOffset fromUtc,
        out DateTimeOffset toUtc,
        out int? sellerFilter,
        out int claimUserId,
        out IActionResult? error)
    {
        fromUtc = default;
        toUtc = default;
        sellerFilter = null;
        claimUserId = 0;
        error = null;

        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (idClaim is null || !int.TryParse(idClaim, out claimUserId))
        {
            error = Unauthorized();
            return false;
        }

        if (User.IsInRole(nameof(UserRole.Admin)))
        {
            if (!DateOnly.TryParse(fromRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
                || !DateOnly.TryParse(toRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
            {
                error = BadRequest("Query parameters 'from' and 'to' are required (yyyy-MM-dd).");
                return false;
            }

            if (from > to)
            {
                error = BadRequest("'from' cannot be after 'to'.");
                return false;
            }

            fromUtc = StartOfDayUtc(from);
            toUtc = EndOfDayUtc(to);
            sellerFilter = sellerUserIdQuery;
            return true;
        }

        if (User.IsInRole(nameof(UserRole.Seller)))
        {
            if (!DateOnly.TryParse(fromRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
                || !DateOnly.TryParse(toRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                from = today;
                to = today;
            }

            if (from > to)
            {
                error = BadRequest("'from' cannot be after 'to'.");
                return false;
            }

            fromUtc = StartOfDayUtc(from);
            toUtc = EndOfDayUtc(to);
            sellerFilter = claimUserId;
            return true;
        }

        error = Forbid();
        return false;
    }

    private static string CsvEscape(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        if (s.Contains('"') || s.Contains(',') || s.Contains('\r') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        return s;
    }
}
