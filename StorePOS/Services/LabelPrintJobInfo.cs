namespace StorePOS.Services;

/// <summary>Describes the raster job sent to the thermal printer (one label).</summary>
public sealed record LabelPrintJobInfo(
    string? PrinterName,
    int WidthMm,
    int HeightMm,
    int Dpi,
    int PixelWidth,
    int PixelHeight,
    string? DriverPaperSummary)
{
    public string Summary =>
        $"{Dpi} DPI · {PixelWidth}×{PixelHeight} px · {WidthMm}×{HeightMm} mm · 0 margins";
}
