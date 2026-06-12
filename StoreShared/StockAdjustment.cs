namespace StoreShared;

public class StockAdjustment
{
    public int Id { get; set; }
    public int SkuId { get; set; }
    public int QuantityDelta { get; set; }
    public string Reason { get; set; } = "";
    public int? SaleEventId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string PerformedBy { get; set; } = "";

    public SKU? Sku { get; set; }
    public SaleEvent? SaleEvent { get; set; }
}
