namespace StoreShared.Receipt;

public sealed record ReceiptDto(
    string ReceiptNumber,
    DateTime Date,
    string? StoreName,
    string? StoreAddress,
    string? ReceiptLandline,
    string? ReceiptPhone,
    IReadOnlyList<ReceiptLineDto> Lines,
    decimal Subtotal,
    decimal Discount,
    decimal Tax,
    decimal Total);
