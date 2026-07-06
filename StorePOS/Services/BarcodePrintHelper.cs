using Microsoft.JSInterop;

namespace StorePOS.Services;

public sealed class BarcodePrintHelper
{
    private readonly IBarcodePrintService _native;
    private readonly IJSRuntime _js;

    public BarcodePrintHelper(IBarcodePrintService native, IJSRuntime js)
    {
        _native = native;
        _js = js;
    }

    public bool HasSavedPrinter =>
        _native.IsSupported && !string.IsNullOrWhiteSpace(_native.GetSelectedPrinter());

    private bool UsesBrowserPrint => !HasSavedPrinter;

    /// <summary>Call synchronously at the start of a click handler, before any await, when browser print will follow a fetch.</summary>
    public Task BeginBrowserPrintAsync()
    {
        if (!UsesBrowserPrint)
            return Task.CompletedTask;

        return _js.InvokeVoidAsync("storePrintBarcodeBegin").AsTask();
    }

    public Task CancelBrowserPrintAsync()
    {
        if (!UsesBrowserPrint)
            return Task.CompletedTask;

        return _js.InvokeVoidAsync("storePrintBarcodeCancel").AsTask();
    }

    public async Task<(bool Ok, bool UsedNative, string? Error)> PrintAsync(string pngBase64, int count)
    {
        if (_native.IsSupported && !string.IsNullOrWhiteSpace(_native.GetSelectedPrinter()))
        {
            var ok = await _native.PrintPngBase64Async(pngBase64, count);
            if (ok)
                return (true, true, null);

            return (false, true,
                "Print failed — check that the barcode printer is on, the label size matches your media, " +
                "and the saved printer name matches System Settings → Printers. " +
                "Details: ~/Library/Application Support/Store POS/label-debug/print.log");
        }

        var jsOk = await _js.InvokeAsync<bool>(
            "storePrintBarcodeFinishPending",
            $"data:image/png;base64,{pngBase64}",
            count);
        if (!jsOk)
            return (false, false, "Could not open the print dialog.");

        return (true, false, null);
    }

    public LabelPrintJobInfo GetPrintJobInfo() => _native.GetPrintJobInfo();

    public string GetCalibrationFramePngBase64() => _native.GetCalibrationFramePngBase64();

    public Task<string?> SaveCalibrationPreviewAsync() => _native.SaveCalibrationPreviewAsync();

    public async Task<(bool Ok, string? Error)> PrintCalibrationFrameAsync(int count)
    {
        if (!_native.IsSupported || string.IsNullOrWhiteSpace(_native.GetSelectedPrinter()))
            return (false, "Select and save a barcode printer first.");

        var copies = Math.Clamp(count, 1, 500);
        var ok = await _native.PrintCalibrationFrameAsync(copies);
        return ok
            ? (true, null)
            : (false, "Calibration print failed — check printer and label size.");
    }
}
