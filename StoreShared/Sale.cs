namespace StoreShared;

public class Sale
{
    public int Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public int UserId { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public string ReceiptNumber { get; set; } = "";
    public bool IsFullyRefunded { get; set; }

    public User? User { get; set; }
    public ICollection<SaleItem> Items { get; set; } = new List<SaleItem>();
    public ICollection<SaleEvent> Events { get; set; } = new List<SaleEvent>();
}
