using Microsoft.EntityFrameworkCore;
using StoreAPI.Data;
using StoreShared;
using StoreShared.Sales;

namespace StoreAPI.Services;

public sealed class RefundService
{
    private readonly StoreDbContext _db;
    private readonly StockService _stock;

    public RefundService(StoreDbContext db, StockService stock)
    {
        _db = db;
        _stock = stock;
    }

    public async Task<SaleByReceiptDto?> GetSaleByReceiptAsync(string receiptNumber, CancellationToken ct = default)
    {
        if (!ReceiptNumberFormat.TryParseSaleId(receiptNumber, out var saleId))
            return null;

        var sale = await LoadSaleAsync(saleId, ct);
        return sale is null ? null : await MapSaleByReceiptAsync(sale, ct);
    }

    public async Task<RefundHistoryDto?> GetRefundHistoryAsync(string receiptNumber, CancellationToken ct = default)
    {
        var saleDto = await GetSaleByReceiptAsync(receiptNumber, ct);
        if (saleDto is null)
            return null;

        var events = await _db.SaleEvents.AsNoTracking()
            .Where(e => e.ReceiptNumber == saleDto.ReceiptNumber
                        && (e.EventType == nameof(SaleEventType.FullRefund)
                            || e.EventType == nameof(SaleEventType.PartialRefund)))
            .Include(e => e.Lines)
            .OrderByDescending(e => e.Timestamp)
            .ToListAsync(ct);

        return new RefundHistoryDto(saleDto, events.Select(MapEvent).ToList());
    }

    public async Task<SaleReceiptHistoryDto?> GetReceiptHistoryAsync(string receiptNumber, CancellationToken ct = default)
    {
        if (!ReceiptNumberFormat.TryParseSaleId(receiptNumber, out var saleId))
            return null;

        var sale = await LoadSaleAsync(saleId, ct);
        if (sale is null)
            return null;

        var saleDto = await MapSaleByReceiptAsync(sale, ct);
        var events = await _db.SaleEvents.AsNoTracking()
            .Where(e => e.ReceiptNumber == sale.ReceiptNumber)
            .Include(e => e.Lines)
            .OrderBy(e => e.Timestamp)
            .ToListAsync(ct);

        return new SaleReceiptHistoryDto(saleDto, events.Select(MapEvent).ToList());
    }

    public async Task<RefundResultDto> ProcessRefundAsync(RefundRequestDto request, string performedBy, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ReceiptNumber))
            return Fail("Receipt number is required.");

        if (!ReceiptNumberFormat.TryParseSaleId(request.ReceiptNumber, out var saleId))
            return Fail("Receipt ID not found — please check and try again.");

        var actor = (performedBy ?? "").Trim();
        if (string.IsNullOrWhiteSpace(actor))
            return Fail("User not authenticated.");

        var sale = await _db.Sales
            .Include(s => s.Items).ThenInclude(i => i.SKU!).ThenInclude(sku => sku.ProductModel)
            .FirstOrDefaultAsync(s => s.Id == saleId, ct);

        if (sale is null)
            return Fail("Receipt ID not found — please check and try again.");

        if (sale.IsFullyRefunded)
            return Fail("This receipt has already been fully refunded");

        var refundedBySku = await GetRefundedQuantitiesAsync(sale.Id, ct);
        var refundedAmountBySku = await GetRefundedAmountsBySkuAsync(sale.Id, ct);
        var refundPricingBySku = BuildRefundPricingBySku(sale);
        var linesToRefund = new List<(SaleItem Item, int Qty, decimal LineTotal)>();

        if (request.Type == RefundType.Full)
        {
            foreach (var item in sale.Items)
            {
                var already = refundedBySku.GetValueOrDefault(item.SKUId);
                var available = item.Quantity - already;
                if (available <= 0)
                    continue;
                var pricing = refundPricingBySku[item.SKUId];
                var alreadyQty = refundedBySku.GetValueOrDefault(item.SKUId);
                var alreadyAmount = refundedAmountBySku.GetValueOrDefault(item.SKUId);
                var lineTotal = RefundPricingCalculator.ComputeRefundAmount(
                    pricing.NetLineTotal,
                    item.Quantity,
                    available,
                    alreadyQty,
                    alreadyAmount);
                linesToRefund.Add((item, available, lineTotal));
            }

            if (linesToRefund.Count == 0)
                return Fail("This receipt has already been fully refunded");
        }
        else
        {
            if (request.Lines is null || request.Lines.Count == 0)
                return Fail("Partial refund requires at least one line.");

            foreach (var reqLine in request.Lines)
            {
                if (reqLine.QuantityToRefund <= 0)
                    return Fail("Each refund line must have quantity greater than zero.");

                var item = sale.Items.FirstOrDefault(i => i.SKUId == reqLine.SkuId);
                if (item is null)
                    return Fail($"SKU #{reqLine.SkuId} is not on this receipt.");

                var already = refundedBySku.GetValueOrDefault(item.SKUId);
                var available = item.Quantity - already;
                if (reqLine.QuantityToRefund > available)
                    return Fail($"Quantity exceeds available to refund for {item.SKU?.ProductModel?.Name ?? "item"} (max {available}).");

                var pricing = refundPricingBySku[item.SKUId];
                var alreadyQty = refundedBySku.GetValueOrDefault(item.SKUId);
                var alreadyAmount = refundedAmountBySku.GetValueOrDefault(item.SKUId);
                var lineTotal = RefundPricingCalculator.ComputeRefundAmount(
                    pricing.NetLineTotal,
                    item.Quantity,
                    reqLine.QuantityToRefund,
                    alreadyQty,
                    alreadyAmount);
                linesToRefund.Add((item, reqLine.QuantityToRefund, lineTotal));
            }
        }

        var grossRefund = linesToRefund.Sum(x => x.LineTotal);

        var partialCount = await _db.SaleEvents.CountAsync(
            e => e.SaleId == sale.Id
                 && (e.EventType == nameof(SaleEventType.PartialRefund)
                     || e.EventType == nameof(SaleEventType.FullRefund)),
            ct);

        // Refund line totals are based on the paid amount after sale-level discount allocation.
        decimal amountRefunded = request.Type == RefundType.Full && partialCount == 0
            ? sale.TotalAmount
            : grossRefund;

        var refundReceiptNumber = request.Type == RefundType.Full && partialCount == 0
            ? ReceiptNumberFormat.ForRefund(sale.Id)
            : ReceiptNumberFormat.ForRefund(sale.Id, partialCount + 1);

        var eventType = request.Type == RefundType.Full
            ? SaleEventType.FullRefund
            : SaleEventType.PartialRefund;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var saleEvent = new SaleEvent
            {
                SaleId = sale.Id,
                ReceiptNumber = sale.ReceiptNumber,
                EventType = eventType.ToString(),
                Timestamp = DateTimeOffset.UtcNow,
                PerformedBy = actor,
                Note = null,
                AmountAffected = amountRefunded,
                RefundReceiptNumber = refundReceiptNumber,
            };

            foreach (var (item, qty, lineTotal) in linesToRefund)
            {
                var eventLine = new SaleEventLine
                {
                    SkuId = item.SKUId,
                    ProductName = item.SKU?.ProductModel?.Name ?? "—",
                    Size = item.SKU?.Size.ToString() ?? "",
                    Quantity = qty,
                    UnitPrice = item.UnitPrice,
                    UnitCost = item.UnitCost,
                    LineTotal = lineTotal,
                };
                saleEvent.Lines.Add(eventLine);

                var (ok, error) = await _stock.TryAddAsync(item.SKUId, qty, ct);
                if (!ok)
                {
                    await tx.RollbackAsync(ct);
                    return Fail(error ?? "Stock restore failed.");
                }

                _db.StockAdjustments.Add(new StockAdjustment
                {
                    SkuId = item.SKUId,
                    QuantityDelta = qty,
                    Reason = $"Refund: {refundReceiptNumber}",
                    SaleEvent = saleEvent,
                    Timestamp = saleEvent.Timestamp,
                    PerformedBy = actor,
                });
            }

            _db.SaleEvents.Add(saleEvent);

            foreach (var (item, qty, _) in linesToRefund)
                refundedBySku[item.SKUId] = refundedBySku.GetValueOrDefault(item.SKUId) + qty;

            var allRefunded = sale.Items.All(i =>
                refundedBySku.GetValueOrDefault(i.SKUId) >= i.Quantity);

            if (request.Type == RefundType.Full || allRefunded)
                sale.IsFullyRefunded = true;

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return new RefundResultDto(true, null, amountRefunded, refundReceiptNumber);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            return Fail(ex.Message);
        }
    }

    public async Task<Dictionary<int, int>> GetRefundedQuantitiesAsync(int saleId, CancellationToken ct = default)
    {
        var refundEvents = await _db.SaleEvents.AsNoTracking()
            .Where(e => e.SaleId == saleId
                        && (e.EventType == nameof(SaleEventType.FullRefund)
                            || e.EventType == nameof(SaleEventType.PartialRefund)))
            .Include(e => e.Lines)
            .ToListAsync(ct);

        var map = new Dictionary<int, int>();
        foreach (var ev in refundEvents)
        {
            foreach (var line in ev.Lines)
                map[line.SkuId] = map.GetValueOrDefault(line.SkuId) + line.Quantity;
        }

        return map;
    }

    public async Task<Dictionary<int, decimal>> GetRefundedAmountsBySkuAsync(int saleId, CancellationToken ct = default)
    {
        var refundEvents = await _db.SaleEvents.AsNoTracking()
            .Where(e => e.SaleId == saleId
                        && (e.EventType == nameof(SaleEventType.FullRefund)
                            || e.EventType == nameof(SaleEventType.PartialRefund)))
            .Include(e => e.Lines)
            .ToListAsync(ct);

        var map = new Dictionary<int, decimal>();
        foreach (var ev in refundEvents)
        {
            foreach (var line in ev.Lines)
                map[line.SkuId] = map.GetValueOrDefault(line.SkuId) + line.LineTotal;
        }

        return map;
    }

    public async Task<decimal> GetTotalRefundedAsync(int saleId, CancellationToken ct = default) =>
        await _db.SaleEvents.AsNoTracking()
            .Where(e => e.SaleId == saleId
                        && (e.EventType == nameof(SaleEventType.FullRefund)
                            || e.EventType == nameof(SaleEventType.PartialRefund)))
            .SumAsync(e => e.AmountAffected ?? 0, ct);

    private async Task<Sale?> LoadSaleAsync(int saleId, CancellationToken ct) =>
        await _db.Sales.AsNoTracking()
            .Include(s => s.User)
            .Include(s => s.Items).ThenInclude(i => i.SKU!).ThenInclude(sku => sku.ProductModel)
            .FirstOrDefaultAsync(s => s.Id == saleId, ct);

    private async Task<SaleByReceiptDto> MapSaleByReceiptAsync(Sale sale, CancellationToken ct)
    {
        var refunded = await GetRefundedQuantitiesAsync(sale.Id, ct);
        var refundedAmounts = await GetRefundedAmountsBySkuAsync(sale.Id, ct);
        var refundPricingBySku = BuildRefundPricingBySku(sale);
        var lines = sale.Items.OrderBy(i => i.Id).Select(i =>
        {
            var already = refunded.GetValueOrDefault(i.SKUId);
            var alreadyAmount = refundedAmounts.GetValueOrDefault(i.SKUId);
            var available = i.Quantity - already;
            var pricing = refundPricingBySku[i.SKUId];
            var refundLineTotal = RefundPricingCalculator.ComputeRefundAmount(
                pricing.NetLineTotal,
                i.Quantity,
                available,
                already,
                alreadyAmount);
            return new SaleLineRefundableDto(
                i.SKUId,
                i.SKU?.ProductModel?.Name ?? "—",
                i.SKU?.Size.ToString() ?? "",
                i.SKU?.Barcode ?? "",
                i.Quantity,
                already,
                available,
                i.UnitPrice,
                i.UnitPrice * i.Quantity,
                pricing.NetLineTotal,
                alreadyAmount,
                pricing.RefundUnitPrice,
                refundLineTotal);
        }).ToList();

        return new SaleByReceiptDto(
            sale.ReceiptNumber,
            sale.Id,
            sale.Timestamp,
            sale.User?.Username ?? "",
            sale.TotalAmount + sale.DiscountAmount,
            sale.DiscountAmount,
            sale.TotalAmount,
            sale.IsFullyRefunded,
            lines);
    }

    public static SaleEventDto MapEvent(SaleEvent e) =>
        new(
            e.Id,
            e.ReceiptNumber,
            Enum.TryParse<SaleEventType>(e.EventType, out var t) ? t : SaleEventType.NoteAdded,
            e.Timestamp.UtcDateTime,
            e.PerformedBy,
            e.Note,
            e.AmountAffected,
            e.RefundReceiptNumber,
            e.Lines.Select(l => new SaleEventLineDto(
                l.SkuId,
                l.ProductName,
                l.Size,
                l.Quantity,
                l.UnitPrice,
                l.LineTotal)).ToList());

    private static Dictionary<int, RefundLinePricing> BuildRefundPricingBySku(Sale sale)
    {
        var items = sale.Items.OrderBy(i => i.Id)
            .Select(i => (i.SKUId, i.Quantity, i.UnitPrice))
            .ToList();
        var pricing = RefundPricingCalculator.BuildLinePricing(items, sale.DiscountAmount, sale.TotalAmount);
        return pricing.ToDictionary(p => p.SkuId, p => new RefundLinePricing(p.RefundUnitPrice, p.NetLineTotal));
    }

    private sealed record RefundLinePricing(decimal RefundUnitPrice, decimal NetLineTotal);

    private static RefundResultDto Fail(string error) =>
        new(false, error, 0, "");
}
