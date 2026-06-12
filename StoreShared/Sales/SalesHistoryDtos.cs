using StoreShared;

namespace StoreShared.Sales;

public sealed record SaleHistoryRowDto(
    int Id,
    string ReceiptNumber,
    DateTimeOffset Timestamp,
    string CashierUsername,
    int LineCount,
    int UnitCount,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TotalAmount,
    bool IsFullyRefunded,
    decimal TotalRefunded);

public sealed record SaleLineDetailDto(
    string ProductModelName,
    ClothingSize Size,
    string Barcode,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record SaleHistoryDetailDto(
    SaleHistoryRowDto Sale,
    IReadOnlyList<SaleLineDetailDto> Lines);

public sealed record SalesSummaryDto(
    decimal TodayRevenue,
    int RangeTransactionCount,
    decimal RangeAverageSale,
    string? TopModelName,
    int TopModelQuantitySold,
    decimal RangeTotalRevenue,
    decimal RangeRefunded,
    decimal RangeNetRevenue,
    decimal TodayRefunded,
    decimal TodayNetRevenue,
    decimal RangeTotalCost,
    decimal RangeNetProfit,
    decimal TodayTotalCost,
    decimal TodayNetProfit);

public sealed record PagedSalesResult(
    IReadOnlyList<SaleHistoryRowDto> Items,
    int TotalCount,
    int Page,
    int PageSize);
