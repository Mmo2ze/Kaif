namespace StoreShared;

public class SaleItem
{
    public int Id { get; set; }
    public int SaleId { get; set; }
    public int SKUId { get; set; }
    public int Quantity { get; set; }
    /// <summary>Price charged per unit at checkout.</summary>
    public decimal UnitPrice { get; set; }
    /// <summary>Buy price snapshot at checkout (from SKU.BuyPrice).</summary>
    public decimal UnitCost { get; set; }

    public Sale? Sale { get; set; }
    public SKU? SKU { get; set; }
}
