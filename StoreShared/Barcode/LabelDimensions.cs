namespace StoreShared.Barcode;

public static class LabelDimensions
{
    public const int DefaultWidthMm = 35;
    public const int DefaultHeightMm = 25;
    public const int PrintDpi = 203;

    /// <summary>35mm × 25mm @ 203 DPI.</summary>
    public const int WidthPx = 280;

    public const int HeightPx = 200;

    public static int MmToPixels(int mm) =>
        (int)Math.Round(mm / 25.4 * PrintDpi);
}
