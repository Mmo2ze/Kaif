namespace StoreAPI.Options;

public sealed class PosOptions
{
    public const string SectionName = "Pos";

    public bool AllowSellerDiscount { get; set; }

    /// <summary>Stock at or above this level is not highlighted as low (exclusive of zero-stock red).</summary>
    public int LowStockThreshold { get; set; } = 5;

    /// <summary>Plain-text PIN for sellers to authorize a discount when AllowSellerDiscount is false (dev/demo).</summary>
    public string AdminDiscountPin { get; set; } = "0000";
}
