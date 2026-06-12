namespace StoreShared.Stock;

public sealed record StockAdjustmentDto(
    int Id,
    int SkuId,
    string ModelName,
    string Size,
    string Barcode,
    int QuantityDelta,
    string Reason,
    DateTimeOffset Timestamp,
    string PerformedBy);

public sealed record PagedStockAdjustmentsResult(
    IReadOnlyList<StockAdjustmentDto> Items,
    int TotalCount,
    int Page,
    int PageSize);
