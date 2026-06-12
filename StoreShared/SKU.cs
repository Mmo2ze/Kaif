namespace StoreShared;

/// <summary>
/// One row per product model + size. Color is not tracked; barcode is unique per SKU.
/// </summary>
public class SKU
{
    public int Id { get; set; }
    public int ProductModelId { get; set; }
    public ClothingSize Size { get; set; }
    /// <summary>EAN-8 (8 digits, prefix 2) — unique per SKU.</summary>
    public string Barcode { get; set; } = string.Empty;
    public int Stock { get; set; }
    /// <summary>Cost paid to acquire one unit (for profit reporting).</summary>
    public decimal BuyPrice { get; set; }
    /// <summary>Regular sell price.</summary>
    public decimal UnitPrice { get; set; }
    /// <summary>Discounted price while on sale; null when not on sale. Must be below UnitPrice.</summary>
    public decimal? SalePrice { get; set; }

    public ProductModel? ProductModel { get; set; }
    public ICollection<SaleItem> SaleItems { get; set; } = new List<SaleItem>();
}
