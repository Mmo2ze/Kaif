namespace StoreShared.Receipt;

public sealed record ReceiptLineDto(
    string ProductName,
    string Size,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    decimal? RegularUnitPrice = null);
