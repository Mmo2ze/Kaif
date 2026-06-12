namespace StoreShared.Catalog;

public sealed record CreateProductModelRequest(
    string Name,
    string? Description,
    decimal BuyPrice = 0,
    decimal UnitPrice = 0,
    decimal? SalePrice = null);

public sealed record UpdateProductModelRequest(string Name, string? Description);

public sealed record UpdateProductPriceRequest(decimal BuyPrice, decimal UnitPrice, decimal? SalePrice);

public sealed record CreateSkuRequest(int ProductModelId, ClothingSize Size, int Stock = 0);

public sealed record ProductModelSummaryDto(
    int Id,
    string Name,
    string? Description,
    int SkuCount,
    decimal BuyPrice,
    decimal UnitPrice,
    decimal? SalePrice);

/// <summary>SKU row for catalog lists without barcode PNG; prices live on the product model.</summary>
public sealed record ProductSkuListRowDto(int Id, ClothingSize Size, string Barcode, int Stock);

public sealed record SkuDetailDto(
    int Id,
    int ProductModelId,
    string? ProductName,
    ClothingSize Size,
    string Barcode,
    string BarcodePngBase64,
    int Stock,
    decimal BuyPrice,
    decimal UnitPrice,
    decimal? SalePrice = null);
