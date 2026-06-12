namespace StorePOS.Services;

public sealed class UnsupportedBarcodePrintService : IBarcodePrintService
{
    public bool IsSupported => false;

    public IReadOnlyList<string> GetInstalledPrinters() => Array.Empty<string>();

    public string? GetSelectedPrinter() => null;

    public void SetSelectedPrinter(string? printerName) { }

    public int GetLabelWidthMm() => 35;

    public int GetLabelHeightMm() => 25;

    public void SetLabelSizeMm(int widthMm, int heightMm) { }

    public string? GetDriverPaperSummary(string? printerName = null) => null;

    public Task<bool> PrintPngBase64Async(string pngBase64, int count, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public LabelPrintJobInfo GetPrintJobInfo(string? printerName = null) =>
        new(null, GetLabelWidthMm(), GetLabelHeightMm(), 203, 0, 0, null);

    public string GetCalibrationFramePngBase64() => "";

    public Task<string?> SaveCalibrationPreviewAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task<bool> PrintCalibrationFrameAsync(int count, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
