using StoreShared.Receipt;
using StoreShared.Sales;

namespace StorePOS.Services;

public interface IReceiptPrintService
{
    bool IsSupported { get; }

    IReadOnlyList<string> GetInstalledPrinters();

    string? GetSelectedPrinter();

    void SetSelectedPrinter(string? printerName);

    bool PrintReceipt(ReceiptDto receipt, string? cashier = null);

    bool PrintRefundReceipt(RefundReceiptDto refund);
}
