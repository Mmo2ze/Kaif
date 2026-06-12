namespace StorePOS.Services;

/// <summary>Silent barcode label printing on this PC (Windows). Printer name is stored locally.</summary>
public interface IBarcodePrintService
{
    bool IsSupported { get; }

    IReadOnlyList<string> GetInstalledPrinters();

    string? GetSelectedPrinter();

    void SetSelectedPrinter(string? printerName);

    int GetLabelWidthMm();

    int GetLabelHeightMm();

    void SetLabelSizeMm(int widthMm, int heightMm);

    /// <summary>What Windows reports for the printer's current paper (driver preferences).</summary>
    string? GetDriverPaperSummary(string? printerName = null);

    Task<bool> PrintPngBase64Async(string pngBase64, int count, CancellationToken cancellationToken = default);

    LabelPrintJobInfo GetPrintJobInfo(string? printerName = null);

    /// <summary>PNG base64 of the exact corner-frame raster sent to the printer (current label size).</summary>
    string GetCalibrationFramePngBase64();

    /// <summary>Writes the corner-frame raster to disk; returns the file path.</summary>
    Task<string?> SaveCalibrationPreviewAsync(CancellationToken cancellationToken = default);

    /// <summary>Prints corner marks at all four edges to check label alignment (one raster per sticker).</summary>
    Task<bool> PrintCalibrationFrameAsync(int count, CancellationToken cancellationToken = default);
}
