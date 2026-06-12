namespace StoreShared.Sales;

public sealed record SaleEventDto(
    int EventId,
    string ReceiptNumber,
    SaleEventType EventType,
    DateTime Timestamp,
    string? PerformedBy,
    string? Note,
    decimal? AmountAffected,
    string? RefundReceiptNumber,
    IReadOnlyList<SaleEventLineDto>? Lines);

public sealed record SaleEventLineDto(
    int SkuId,
    string ProductName,
    string Size,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record RefundRequestDto(
    string ReceiptNumber,
    RefundType Type,
    IReadOnlyList<RefundLineDto>? Lines);

public sealed record RefundLineDto(
    int SkuId,
    int QuantityToRefund);

public sealed record RefundResultDto(
    bool Success,
    string? Error,
    decimal AmountRefunded,
    string RefundReceiptNumber);

public sealed record RefundReceiptDto(
    string RefundReceiptNumber,
    string OriginalReceiptNumber,
    DateTime Timestamp,
    string TypeLabel,
    string PerformedBy,
    string? Reason,
    IReadOnlyList<SaleEventLineDto> Lines,
    decimal AmountRefunded,
    string? StoreName,
    string? ReceiptLandline = null,
    string? ReceiptPhone = null);

public sealed record SaleByReceiptDto(
    string ReceiptNumber,
    int SaleId,
    DateTimeOffset Timestamp,
    string CashierUsername,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TotalAmount,
    bool IsFullyRefunded,
    IReadOnlyList<SaleLineRefundableDto> Lines);

public sealed record SaleLineRefundableDto(
    int SkuId,
    string ProductName,
    string Size,
    string Barcode,
    int OriginalQuantity,
    int AlreadyRefunded,
    int QuantityAvailable,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record RefundHistoryDto(
    SaleByReceiptDto OriginalSale,
    IReadOnlyList<SaleEventDto> RefundEvents);

public sealed record SaleReceiptHistoryDto(
    SaleByReceiptDto? OriginalSale,
    IReadOnlyList<SaleEventDto> Events);

public sealed record PagedSaleEventsResult(
    IReadOnlyList<SaleEventDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record SalesStatisticsDto(
    DateOnly From,
    DateOnly To,
    int TotalSalesCount,
    decimal TotalRevenue,
    decimal AverageOrderValue,
    int TotalItemsSold,
    int TotalRefundsCount,
    decimal TotalAmountRefunded,
    decimal RefundRatePercent,
    decimal TotalCost,
    decimal NetProfit,
    string? MostRefundedProductName,
    int MostRefundedProductCount,
    IReadOnlyList<CashierMetricDto> SalesPerCashier,
    IReadOnlyList<CashierMetricDto> RefundsPerCashier,
    IReadOnlyList<DailyMetricDto> SalesPerDay,
    IReadOnlyList<DailyMetricDto> RefundsPerDay,
    IReadOnlyList<DailyMetricDto> ProfitPerDay,
    IReadOnlyList<TopSkuMetricDto> TopSellingSkus,
    IReadOnlyList<TopSkuMetricDto> TopRefundedSkus);

public sealed record CashierMetricDto(string Username, int Count, decimal Amount);

public sealed record DailyMetricDto(DateOnly Date, decimal Amount, int Count);

public sealed record TopSkuMetricDto(
    int SkuId,
    string ProductName,
    string Size,
    int Quantity,
    decimal Revenue,
    decimal Cost,
    decimal Profit);
