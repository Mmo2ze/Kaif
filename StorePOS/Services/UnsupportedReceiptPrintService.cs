using StoreShared.Receipt;
using StoreShared.Sales;

namespace StorePOS.Services;

public sealed class UnsupportedReceiptPrintService : IReceiptPrintService
{
    public bool IsSupported => false;

    public IReadOnlyList<string> GetInstalledPrinters() => Array.Empty<string>();

    public string? GetSelectedPrinter() => null;

    public void SetSelectedPrinter(string? printerName) { }

    public bool PrintReceipt(ReceiptDto receipt, string? cashier = null) => false;

    public bool PrintRefundReceipt(RefundReceiptDto refund) => false;
}
