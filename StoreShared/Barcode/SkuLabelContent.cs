namespace StoreShared.Barcode;

public sealed record SkuLabelContent(
    string StoreName,
    string ProductName,
    string PriceText,
    string Barcode,
    string? OriginalPriceText = null);
