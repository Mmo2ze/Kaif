using System.Text;
using SkiaSharp;
using StorePOS.Services;

namespace StorePOS.Platforms.MacCatalyst;

public sealed class MacBarcodePrintService : IBarcodePrintService
{
    private const string PrefKey = "barcode_printer_name";
    private const string PrefLabelWidthMm = "barcode_label_width_mm";
    private const string PrefLabelHeightMm = "barcode_label_height_mm";

    private const int DefaultLabelWidthMm = 35;
    private const int DefaultLabelHeightMm = 25;
    private const int LabelDpi = 203;

    public bool IsSupported => true;

    public IReadOnlyList<string> GetInstalledPrinters() => MacCupsPrintHelper.GetInstalledPrinters();

    public string? GetSelectedPrinter()
    {
        var name = Preferences.Get(PrefKey, "");
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    public void SetSelectedPrinter(string? printerName) =>
        Preferences.Set(PrefKey, string.IsNullOrWhiteSpace(printerName) ? "" : printerName.Trim());

    public int GetLabelWidthMm() => Preferences.Get(PrefLabelWidthMm, DefaultLabelWidthMm);

    public int GetLabelHeightMm() => Preferences.Get(PrefLabelHeightMm, DefaultLabelHeightMm);

    public void SetLabelSizeMm(int widthMm, int heightMm)
    {
        Preferences.Set(PrefLabelWidthMm, Math.Clamp(widthMm, 20, 108));
        Preferences.Set(PrefLabelHeightMm, Math.Clamp(heightMm, 15, 150));
    }

    public string? GetDriverPaperSummary(string? printerName = null) =>
        MacCupsPrintHelper.GetPaperSummary(printerName ?? GetSelectedPrinter() ?? "");

    public Task<bool> PrintPngBase64Async(string pngBase64, int count, CancellationToken cancellationToken = default)
    {
        var printer = GetSelectedPrinter();
        if (string.IsNullOrWhiteSpace(printer))
            return Task.FromResult(false);

        var copies = Math.Clamp(count, 1, 500);
        var widthMm = GetLabelWidthMm();
        var heightMm = GetLabelHeightMm();
        return Task.Run(() => PrintOnPrinter(printer, pngBase64, copies, widthMm, heightMm), cancellationToken);
    }

    public LabelPrintJobInfo GetPrintJobInfo(string? printerName = null)
    {
        printerName ??= GetSelectedPrinter();
        var widthMm = GetLabelWidthMm();
        var heightMm = GetLabelHeightMm();
        return new LabelPrintJobInfo(
            printerName,
            widthMm,
            heightMm,
            LabelDpi,
            MmToPixels(widthMm, LabelDpi),
            MmToPixels(heightMm, LabelDpi),
            GetDriverPaperSummary(printerName));
    }

    public string GetCalibrationFramePngBase64()
    {
        using var bmp = RenderCalibrationBitmap(GetLabelWidthMm(), GetLabelHeightMm());
        return BitmapToPngBase64(bmp);
    }

    public Task<string?> SaveCalibrationPreviewAsync(CancellationToken cancellationToken = default) =>
        Task.Run(SaveCalibrationPreviewCore, cancellationToken);

    public Task<bool> PrintCalibrationFrameAsync(int count, CancellationToken cancellationToken = default)
    {
        var printer = GetSelectedPrinter();
        if (string.IsNullOrWhiteSpace(printer))
            return Task.FromResult(false);

        var copies = Math.Clamp(count, 1, 500);
        var widthMm = GetLabelWidthMm();
        var heightMm = GetLabelHeightMm();
        return Task.Run(() => PrintCalibrationOnPrinter(printer, copies, widthMm, heightMm), cancellationToken);
    }

    private string? SaveCalibrationPreviewCore()
    {
        var info = GetPrintJobInfo();
        using var bmp = RenderCalibrationBitmap(info.WidthMm, info.HeightMm);
        var dir = Path.Combine(FileSystem.AppDataDirectory, "label-debug");
        Directory.CreateDirectory(dir);
        var pngPath = Path.Combine(dir, "printer-raster-preview.png");
        SavePng(bmp, pngPath);

        var txtPath = Path.Combine(dir, "printer-raster-preview.txt");
        File.WriteAllText(txtPath, BuildPreviewText(info, pngPath), Encoding.UTF8);
        return pngPath;
    }

    private static string BuildPreviewText(LabelPrintJobInfo info, string pngPath) =>
        $"""
        What the printer receives (one label)
        =====================================
        {info.Summary}
        Printer: {info.PrinterName ?? "(none)"}
        Driver paper: {info.DriverPaperSummary ?? "(unknown)"}

        Format: PNG via CUPS (one job per batch); ESC/POS raw fallback for compatible printers.
        Corner-frame test uses the full label; product barcodes are scaled inside with a small inner margin.

        Preview image: {pngPath}
        Saved: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
        """;

    private static bool PrintCalibrationOnPrinter(string printerName, int copies, int widthMm, int heightMm)
    {
        using var label = RenderCalibrationBitmap(widthMm, heightMm);
        return PrintLabelBitmap(printerName, label, copies, widthMm, heightMm);
    }

    private static bool PrintOnPrinter(string printerName, string pngBase64, int copies, int widthMm, int heightMm)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(pngBase64);
        }
        catch
        {
            return false;
        }

        using var source = SKBitmap.Decode(bytes);
        if (source is null)
            return false;

        using var label = RenderLabelBitmap(source, widthMm, heightMm);
        return PrintLabelBitmap(printerName, label, copies, widthMm, heightMm);
    }

    private static bool PrintLabelBitmap(string printerName, SKBitmap label, int copies, int widthMm, int heightMm)
    {
        if (!MacCupsPrintHelper.PrinterExists(printerName))
        {
            MacCupsPrintHelper.LogPrint($"printer not found in CUPS: {printerName}");
            return false;
        }

        var temp = Path.Combine(Path.GetTempPath(), $"storepos-label-{Guid.NewGuid():N}.png");
        try
        {
            SavePng(label, temp);

            // Label drivers (e.g. Xprinter XP-200B) expect a raster image job like Windows GDI printing.
            if (MacCupsPrintHelper.PrintImageFile(printerName, temp, widthMm, heightMm, copies))
                return true;

            // Some USB thermal printers accept ESC/POS raw; try after PNG so labels still work when raw is wrong.
            var batch = EscPosRasterEncoder.BuildLabelBatch(label, copies);
            return MacCupsPrintHelper.PrintRawBytes(printerName, batch);
        }
        catch (Exception ex)
        {
            MacCupsPrintHelper.LogPrint($"PrintLabelBitmap: {ex.Message}");
            return false;
        }
        finally
        {
            try { File.Delete(temp); } catch { /* best effort */ }
        }
    }

    private static SKBitmap RenderLabelBitmap(SKBitmap source, int widthMm, int heightMm)
    {
        var pixW = Math.Max(1, MmToPixels(widthMm, LabelDpi));
        var pixH = Math.Max(1, MmToPixels(heightMm, LabelDpi));
        var bmp = new SKBitmap(pixW, pixH, SKColorType.Rgb888x, SKAlphaType.Opaque);

        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.White);

            var sameSize = Math.Abs(source.Width - pixW) <= 2 && Math.Abs(source.Height - pixH) <= 2;
            if (sameSize)
            {
                canvas.DrawBitmap(source, SKRect.Create(0, 0, pixW, pixH));
            }
            else
            {
                var pad = Math.Max(4, pixW / 40);
                var inner = new SKRect(pad, pad, pixW - pad, pixH - pad);
                var scale = Math.Min(inner.Width / source.Width, inner.Height / source.Height);
                var w = Math.Max(1, source.Width * scale);
                var h = Math.Max(1, source.Height * scale);
                var x = inner.Left + (inner.Width - w) / 2f;
                var y = inner.Top;
                canvas.DrawBitmap(source, SKRect.Create(0, 0, source.Width, source.Height), SKRect.Create(x, y, w, h));
            }
        }

        return bmp;
    }

    private static SKBitmap RenderCalibrationBitmap(int widthMm, int heightMm)
    {
        var pixW = Math.Max(1, MmToPixels(widthMm, LabelDpi));
        var pixH = Math.Max(1, MmToPixels(heightMm, LabelDpi));
        var arm = Math.Max(10, Math.Min(pixW, pixH) / 7);
        var penWidth = Math.Max(2f, Math.Min(pixW, pixH) / 50f);

        var bmp = new SKBitmap(pixW, pixH, SKColorType.Rgb888x, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint
            {
                Color = SKColors.Black,
                StrokeWidth = penWidth,
                IsAntialias = false,
                Style = SKPaintStyle.Stroke,
            };

            DrawCornerL(canvas, paint, 0, 0, arm, true, true);
            DrawCornerL(canvas, paint, pixW - 1, 0, arm, false, true);
            DrawCornerL(canvas, paint, 0, pixH - 1, arm, true, false);
            DrawCornerL(canvas, paint, pixW - 1, pixH - 1, arm, false, false);

            using var textPaint = new SKPaint
            {
                Color = SKColors.Black,
                IsAntialias = true,
                TextSize = Math.Max(6f, pixH / 14f),
                Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
                TextAlign = SKTextAlign.Center,
            };
            var text = $"{widthMm}×{heightMm} mm";
            canvas.DrawText(text, pixW / 2f, pixH / 2f + textPaint.TextSize / 3f, textPaint);
        }

        return bmp;
    }

    private static void DrawCornerL(
        SKCanvas canvas,
        SKPaint paint,
        float cornerX,
        float cornerY,
        float arm,
        bool fromLeft,
        bool fromTop)
    {
        var hx = fromLeft ? cornerX + arm : cornerX - arm;
        var vy = fromTop ? cornerY + arm : cornerY - arm;
        canvas.DrawLine(cornerX, cornerY, hx, cornerY, paint);
        canvas.DrawLine(cornerX, cornerY, cornerX, vy, paint);
    }

    private static int MmToPixels(int mm, int dpi) =>
        (int)Math.Round(mm / 25.4 * dpi);

    private static void SavePng(SKBitmap bmp, string path)
    {
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    private static string BitmapToPngBase64(SKBitmap bmp)
    {
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return Convert.ToBase64String(data.ToArray());
    }
}
