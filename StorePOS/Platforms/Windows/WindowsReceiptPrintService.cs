using System.Drawing.Printing;
using StorePOS.Services;
using StoreShared.Receipt;
using StoreShared.Sales;

namespace StorePOS.Platforms.Windows;

public sealed class WindowsReceiptPrintService : IReceiptPrintService
{
    private const string PrefKey = "receipt_printer_name";

    private readonly ReceiptLogoStore _logoStore;
    private byte[]? _cachedLogoRaster;
    private long _cachedLogoVersion = -1;

    public WindowsReceiptPrintService(ReceiptLogoStore logoStore)
    {
        _logoStore = logoStore;
    }

    public bool IsSupported => true;

    public IReadOnlyList<string> GetInstalledPrinters()
    {
        var list = new List<string>();
        foreach (string printer in PrinterSettings.InstalledPrinters)
            list.Add(printer);
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    public string? GetSelectedPrinter()
    {
        var name = Preferences.Get(PrefKey, "");
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    public void SetSelectedPrinter(string? printerName) =>
        Preferences.Set(PrefKey, string.IsNullOrWhiteSpace(printerName) ? "" : printerName.Trim());

    public bool PrintReceipt(ReceiptDto receipt, string? cashier = null) =>
        PrintRaw(EscPosReceiptBuilder.BuildReceipt(receipt, GetLogoRaster(), cashier));

    public bool PrintRefundReceipt(RefundReceiptDto refund) =>
        PrintRaw(EscPosReceiptBuilder.BuildRefundReceipt(refund, GetLogoRaster()));

    private byte[]? GetLogoRaster()
    {
        var version = _logoStore.Version;
        if (version == 0)
            return null;

        if (version != _cachedLogoVersion)
        {
            _cachedLogoRaster = EscPosReceiptBuilder.BuildLogoRaster(_logoStore.LogoPath);
            _cachedLogoVersion = version;
        }

        return _cachedLogoRaster;
    }

    private bool PrintRaw(byte[] data)
    {
        var printer = GetSelectedPrinter();
        if (string.IsNullOrWhiteSpace(printer))
            return false;

        return WindowsRawPrinter.SendRaw(printer, data);
    }
}
