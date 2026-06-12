namespace StoreShared.Pos;

public sealed record PosSettingsDto(
    string StoreName,
    string CurrencyLabel,
    bool AllowSellerDiscount,
    int LowStockThreshold,
    string? ReceiptLandline = null,
    string? ReceiptPhone = null);

public sealed record AuthorizeDiscountRequest(string Pin);

public sealed record AuthorizeDiscountResponse(string? DiscountAuthorizationToken);
