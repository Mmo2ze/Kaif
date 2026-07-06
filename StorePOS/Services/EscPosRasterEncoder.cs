using SkiaSharp;

namespace StorePOS.Services;

/// <summary>ESC/POS GS v 0 raster encoding for thermal printers (labels + receipts).</summary>
public static class EscPosRasterEncoder
{
    private static readonly byte[] Init = [0x1B, 0x40];
    private static readonly byte[] FeedThreeLines = [0x1B, 0x64, 0x03];

    public static byte[] ToEscPosRaster(SKBitmap bmp)
    {
        var w = bmp.Width;
        var h = bmp.Height;
        var bytesPerRow = (w + 7) / 8;
        var data = new byte[bytesPerRow * h];

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var p = bmp.GetPixel(x, y);
                var lum = 0.299 * p.Red + 0.587 * p.Green + 0.114 * p.Blue;
                if (p.Alpha > 128 && lum < 160)
                    data[y * bytesPerRow + (x >> 3)] |= (byte)(0x80 >> (x & 7));
            }
        }

        using var raster = new MemoryStream();
        raster.WriteByte(0x1D);
        raster.WriteByte(0x76);
        raster.WriteByte(0x30);
        raster.WriteByte(0x00);
        raster.WriteByte((byte)(bytesPerRow & 0xFF));
        raster.WriteByte((byte)((bytesPerRow >> 8) & 0xFF));
        raster.WriteByte((byte)(h & 0xFF));
        raster.WriteByte((byte)((h >> 8) & 0xFF));
        raster.Write(data, 0, data.Length);
        return raster.ToArray();
    }

    /// <summary>One spooler job with N identical labels — fast path for USB thermal printers.</summary>
    public static byte[] BuildLabelBatch(SKBitmap bmp, int copies)
    {
        var raster = ToEscPosRaster(bmp);
        using var ms = new MemoryStream(raster.Length * copies + copies * 8);
        for (var i = 0; i < copies; i++)
        {
            ms.Write(Init, 0, Init.Length);
            ms.Write(raster, 0, raster.Length);
            ms.Write(FeedThreeLines, 0, FeedThreeLines.Length);
        }

        return ms.ToArray();
    }
}
