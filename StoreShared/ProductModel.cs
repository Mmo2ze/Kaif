namespace StoreShared;

public class ProductModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Cost paid per unit (same for all sizes).</summary>
    public decimal BuyPrice { get; set; }
    /// <summary>Regular sell price (same for all sizes).</summary>
    public decimal UnitPrice { get; set; }
    /// <summary>Discounted price while on sale; null when not on sale.</summary>
    public decimal? SalePrice { get; set; }

    public ICollection<SKU> Skus { get; set; } = new List<SKU>();
}
