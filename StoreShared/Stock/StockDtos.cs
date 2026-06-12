using StoreShared;

namespace StoreShared.Stock;

public sealed record StockRowDto(
    int SkuId,
    int ProductModelId,
    string ModelName,
    ClothingSize Size,
    string Barcode,
    string BarcodePngBase64,
    int Stock);

public sealed record SetStockRequest(int Quantity);

public sealed record AdjustStockRequest(int Quantity);

public sealed record SaleLineRequest(int SkuId, int Quantity, decimal UnitPrice);

public sealed record CreateSaleRequest(
    IReadOnlyList<SaleLineRequest> Items,
    decimal DiscountAmount,
    string? DiscountAuthorizationToken = null);

public sealed record SaleCreatedDto(int SaleId, decimal TotalAmount, string ReceiptNumber);
