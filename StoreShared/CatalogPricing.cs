namespace StoreShared;

/// <summary>Resolves catalog sell/buy prices from the product model (one price per product).</summary>
public static class CatalogPricing
{
    public static (decimal BuyPrice, decimal UnitPrice, decimal? SalePrice) ForSku(SKU sku)
    {
        var model = sku.ProductModel;
        if (model is not null)
            return (model.BuyPrice, model.UnitPrice, model.SalePrice);

        return (sku.BuyPrice, sku.UnitPrice, sku.SalePrice);
    }

    public static (decimal BuyPrice, decimal UnitPrice, decimal? SalePrice) ForModel(ProductModel model) =>
        (model.BuyPrice, model.UnitPrice, model.SalePrice);
}
