namespace StoreShared.Catalog;

public sealed record CreateProductModelRequest(
    string Name,
    string? Description,
    decimal BuyPrice = 0,
    decimal UnitPrice = 0,
    decimal? SalePrice = null,
    int InitialStock = 0);

public sealed record UpdateProductModelRequest(string Name, string? Description);

public sealed record UpdateProductPriceRequest(decimal BuyPrice, decimal UnitPrice, decimal? SalePrice);

public sealed record CreateSkuRequest(int ProductModelId, ClothingSize Size, int Stock = 0);

public sealed record ProductModelSummaryDto(
    int Id,
    string Name,
    string? Description,
    int SkuId,
    string Barcode,
    string BarcodePngBase64,
    int Stock,
    decimal BuyPrice,
    decimal UnitPrice,
    decimal? SalePrice);

/// <summary>Legacy SKU row; prefer <see cref="ProductModelSummaryDto"/> (one product = one item).</summary>
public sealed record ProductSkuListRowDto(int Id, string Barcode, int Stock);

public sealed record SkuDetailDto(
    int Id,
    int ProductModelId,
    string? ProductName,
    string Barcode,
    string BarcodePngBase64,
    int Stock,
    decimal BuyPrice,
    decimal UnitPrice,
    decimal? SalePrice = null);
