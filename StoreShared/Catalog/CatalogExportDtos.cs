using System.Text.Json.Serialization;

namespace StoreShared.Catalog;

public sealed record CatalogExportFileDto(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("exportedAtUtc")] DateTime ExportedAtUtc,
    [property: JsonPropertyName("products")] IReadOnlyList<CatalogExportProductDto> Products);

/// <summary>Product name, prices, and stock (no sizes).</summary>
public sealed record CatalogExportProductDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("buyPrice")] decimal BuyPrice,
    [property: JsonPropertyName("unitPrice")] decimal UnitPrice,
    [property: JsonPropertyName("salePrice")] decimal? SalePrice,
    [property: JsonPropertyName("stock")] int Stock,
    [property: JsonPropertyName("sizes")] IReadOnlyList<ClothingSize>? Sizes = null,
    [property: JsonPropertyName("skus")] IReadOnlyList<CatalogExportSkuDto>? Skus = null);

/// <summary>Legacy import shape; stock and other fields are ignored.</summary>
public sealed record CatalogExportSkuDto(
    [property: JsonPropertyName("size")] ClothingSize Size);

public sealed record CatalogImportResultDto(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("productsCreated")] int ProductsCreated,
    [property: JsonPropertyName("productsUpdated")] int ProductsUpdated,
    [property: JsonPropertyName("skusCreated")] int SkusCreated,
    [property: JsonPropertyName("skusUpdated")] int SkusUpdated);
