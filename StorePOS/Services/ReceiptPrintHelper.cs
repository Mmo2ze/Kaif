using StoreShared.Receipt;
using StoreShared.Sales;

namespace StorePOS.Services;

public static class RefundReceiptPrintHelper
{
    public static RefundReceiptDto FromRefund(
        RefundResultDto result,
        SaleByReceiptDto original,
        RefundType type,
        string performedBy,
        string? reason,
        IReadOnlyList<SaleEventLineDto> lines,
        string? storeName,
        string? receiptLandline,
        string? receiptPhone) =>
        new(
            result.RefundReceiptNumber,
            original.ReceiptNumber,
            DateTime.Now,
            type == RefundType.Full ? "Full Refund" : "Partial Refund",
            performedBy,
            reason,
            lines,
            result.AmountRefunded,
            storeName,
            receiptLandline,
            receiptPhone);
}

public static class ReceiptPrintHelper
{
    public static ReceiptDto FromSale(
        int saleId,
        DateTimeOffset when,
        string? storeName,
        string? storeAddress,
        string? receiptLandline,
        string? receiptPhone,
        IEnumerable<ReceiptLineInput> lines,
        decimal subtotal,
        decimal discount,
        decimal total,
        string? receiptNumber = null)
    {
        var receiptLines = lines.Select(l => new ReceiptLineDto(
            l.ProductName,
            l.Size,
            l.Quantity,
            l.UnitPrice,
            l.LineTotal,
            l.RegularUnitPrice)).ToList();

        var subtotalBeforeSale = receiptLines.Sum(l => l.Quantity * (l.RegularUnitPrice ?? l.UnitPrice));
        var saleDiscount = Math.Max(0, subtotalBeforeSale - subtotal);

        return new ReceiptDto(
            ReceiptNumber: string.IsNullOrWhiteSpace(receiptNumber)
                ? StoreShared.Sales.ReceiptNumberFormat.ForSale(saleId)
                : receiptNumber.Trim(),
            Date: when.LocalDateTime,
            StoreName: storeName,
            StoreAddress: storeAddress,
            ReceiptLandline: receiptLandline,
            ReceiptPhone: receiptPhone,
            Lines: receiptLines,
            Subtotal: subtotal,
            Discount: discount,
            Tax: 0,
            Total: total,
            SubtotalBeforeSale: subtotalBeforeSale,
            SaleDiscount: saleDiscount);
    }

    public sealed record ReceiptLineInput(
        string ProductName,
        string Size,
        int Quantity,
        decimal UnitPrice,
        decimal LineTotal,
        decimal? RegularUnitPrice = null);
}
