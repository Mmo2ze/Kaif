using StoreShared.Sales;

namespace StoreShared;

public class SaleEvent
{
    public int Id { get; set; }
    public int SaleId { get; set; }
    public string ReceiptNumber { get; set; } = "";
    public string EventType { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
    public string PerformedBy { get; set; } = "";
    public string? Note { get; set; }
    public decimal? AmountAffected { get; set; }
    public string? RefundReceiptNumber { get; set; }

    public Sale? Sale { get; set; }
    public ICollection<SaleEventLine> Lines { get; set; } = new List<SaleEventLine>();
    public ICollection<StockAdjustment> StockAdjustments { get; set; } = new List<StockAdjustment>();

    public SaleEventType ParsedEventType =>
        Enum.TryParse<SaleEventType>(EventType, out var t) ? t : SaleEventType.NoteAdded;
}
