namespace StoreShared;

public class SaleEventLine
{
    public int Id { get; set; }
    public int SaleEventId { get; set; }
    public int SkuId { get; set; }
    public string ProductName { get; set; } = "";
    public string Size { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    /// <summary>Buy price snapshot from the original sale line when refunded.</summary>
    public decimal UnitCost { get; set; }
    public decimal LineTotal { get; set; }

    public SaleEvent? SaleEvent { get; set; }
}
