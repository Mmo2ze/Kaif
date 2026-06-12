namespace StoreShared.Barcode;

public sealed record SkuLabelContent(
    string StoreName,
    string ProductName,
    string SizeText,
    string PriceText,
    string Barcode,
    string? OriginalPriceText = null);
