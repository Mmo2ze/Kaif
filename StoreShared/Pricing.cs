namespace StoreShared;

/// <summary>Shared rule for when a SKU sale price applies.</summary>
public static class Pricing
{
    public static bool IsOnSale(decimal unitPrice, decimal? salePrice) =>
        salePrice is { } s && s > 0 && s < unitPrice;

    /// <summary>The price actually charged: sale price when on sale, otherwise the unit price.</summary>
    public static decimal Effective(decimal unitPrice, decimal? salePrice) =>
        IsOnSale(unitPrice, salePrice) ? salePrice!.Value : unitPrice;
}
