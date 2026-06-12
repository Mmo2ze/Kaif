using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Text;
using StorePOS.Services;
using DrawingImage = System.Drawing.Image;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingPen = System.Drawing.Pen;
using DrawingRectangle = System.Drawing.Rectangle;

namespace StorePOS.Platforms.Windows;

public sealed class WindowsBarcodePrintService : IBarcodePrintService
{
    private const string PrefKey = "barcode_printer_name";
    private const string PrefLabelWidthMm = "barcode_label_width_mm";
    private const string PrefLabelHeightMm = "barcode_label_height_mm";

    private const int DefaultLabelWidthMm = 35;
    private const int DefaultLabelHeightMm = 25;
    private const int LabelDpi = 203;

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

    public int GetLabelWidthMm() =>
        Preferences.Get(PrefLabelWidthMm, DefaultLabelWidthMm);

    public int GetLabelHeightMm() =>
        Preferences.Get(PrefLabelHeightMm, DefaultLabelHeightMm);

    public void SetLabelSizeMm(int widthMm, int heightMm)
    {
        Preferences.Set(PrefLabelWidthMm, Math.Clamp(widthMm, 20, 108));
        Preferences.Set(PrefLabelHeightMm, Math.Clamp(heightMm, 15, 150));
    }

    public string? GetDriverPaperSummary(string? printerName = null)
    {
        printerName ??= GetSelectedPrinter();
        if (string.IsNullOrWhiteSpace(printerName))
            return null;

        try
        {
            using var doc = new PrintDocument { PrinterSettings = { PrinterName = printerName } };
            if (!doc.PrinterSettings.IsValid)
                return null;

            var ps = doc.PrinterSettings.DefaultPageSettings.PaperSize;
            return $"{ps.PaperName} ({HundredthsToMm(ps.Width):0.#}×{HundredthsToMm(ps.Height):0.#} mm)";
        }
        catch
        {
            return null;
        }
    }

    public Task<bool> PrintPngBase64Async(string pngBase64, int count, CancellationToken cancellationToken = default)
    {
        var printer = GetSelectedPrinter();
        if (string.IsNullOrWhiteSpace(printer))
            return Task.FromResult(false);

        var copies = Math.Clamp(count, 1, 500);
        var widthMm = GetLabelWidthMm();
        var heightMm = GetLabelHeightMm();
        return Task.Run(
            () => RunOnStaThread(() => PrintOnPrinter(printer, pngBase64, copies, widthMm, heightMm)),
            cancellationToken);
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
        string? base64 = null;
        var widthMm = GetLabelWidthMm();
        var heightMm = GetLabelHeightMm();
        RunOnStaThread(() =>
        {
            using var bmp = RenderCalibrationBitmap(widthMm, heightMm);
            base64 = BitmapToPngBase64(bmp);
            return true;
        });
        return base64 ?? "";
    }

    public Task<string?> SaveCalibrationPreviewAsync(CancellationToken cancellationToken = default)
    {
        string? path = null;
        return Task.Run(() =>
        {
            RunOnStaThread(() =>
            {
                path = SaveCalibrationPreviewCore();
                return path is not null;
            });
            return path;
        }, cancellationToken);
    }

    public Task<bool> PrintCalibrationFrameAsync(int count, CancellationToken cancellationToken = default)
    {
        var printer = GetSelectedPrinter();
        if (string.IsNullOrWhiteSpace(printer))
            return Task.FromResult(false);

        var copies = Math.Clamp(count, 1, 500);
        var widthMm = GetLabelWidthMm();
        var heightMm = GetLabelHeightMm();
        return Task.Run(
            () => RunOnStaThread(() => PrintCalibrationOnPrinter(printer, copies, widthMm, heightMm)),
            cancellationToken);
    }

    private string? SaveCalibrationPreviewCore()
    {
        var info = GetPrintJobInfo();
        using var bmp = RenderCalibrationBitmap(info.WidthMm, info.HeightMm);
        var dir = Path.Combine(FileSystem.AppDataDirectory, "label-debug");
        Directory.CreateDirectory(dir);
        var pngPath = Path.Combine(dir, "printer-raster-preview.png");
        bmp.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);

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

        Format: 24-bit RGB bitmap, drawn at 0,0 with size {info.WidthMm}×{info.HeightMm} mm in print units.
        Corner-frame test uses the full label; product barcodes are scaled inside with a small inner margin.

        Preview image: {pngPath}
        Saved: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
        """;

    private static bool PrintCalibrationOnPrinter(string printerName, int copies, int widthMm, int heightMm)
    {
        using var label = RenderCalibrationBitmap(widthMm, heightMm);
        for (var copy = 0; copy < copies; copy++)
        {
            if (!PrintSingleLabel(printerName, label, widthMm, heightMm))
                return false;
        }

        return true;
    }

    private static bool RunOnStaThread(Func<bool> action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            return action();

        var result = false;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromMinutes(2));
        if (error is not null)
            throw error;
        return result;
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

        using var ms = new MemoryStream(bytes);
        using var source = DrawingImage.FromStream(ms);
        using var label = RenderLabelBitmap(source, widthMm, heightMm);

        for (var copy = 0; copy < copies; copy++)
        {
            if (!PrintSingleLabel(printerName, label, widthMm, heightMm))
                return false;
        }

        return true;
    }

    /// <summary>Rasterize to exactly one label at 203 DPI so the spooler cannot split across two stickers.</summary>
    private static DrawingBitmap RenderLabelBitmap(DrawingImage source, int widthMm, int heightMm)
    {
        var pixW = Math.Max(1, MmToPixels(widthMm, LabelDpi));
        var pixH = Math.Max(1, MmToPixels(heightMm, LabelDpi));
        var bmp = new DrawingBitmap(pixW, pixH, PixelFormat.Format24bppRgb);
        bmp.SetResolution(LabelDpi, LabelDpi);

        using (var g = DrawingGraphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.White);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.SmoothingMode = SmoothingMode.None;

            var sameSize = Math.Abs(source.Width - pixW) <= 2 && Math.Abs(source.Height - pixH) <= 2;
            if (sameSize)
            {
                g.DrawImage(source, 0, 0, pixW, pixH);
            }
            else
            {
                var pad = Math.Max(4, pixW / 40);
                var inner = new DrawingRectangle(pad, pad, pixW - pad * 2, pixH - pad * 2);
                var scale = Math.Min(
                    (float)inner.Width / source.Width,
                    (float)inner.Height / source.Height);
                var w = Math.Max(1, (int)Math.Round(source.Width * scale));
                var h = Math.Max(1, (int)Math.Round(source.Height * scale));
                var x = inner.Left + (inner.Width - w) / 2;
                var y = inner.Top;
                g.DrawImage(source, x, y, w, h);
            }
        }

        return bmp;
    }

    private static bool PrintSingleLabel(string printerName, DrawingBitmap label, int widthMm, int heightMm)
    {
        using var document = new PrintDocument
        {
            PrintController = new StandardPrintController(),
        };
        document.PrinterSettings.PrinterName = printerName;
        if (!document.PrinterSettings.IsValid)
            return false;

        ApplyLabelPaperSize(document, widthMm, heightMm);

        document.PrintPage += (_, e) =>
        {
            if (e.Graphics is null)
                return;

            var g = e.Graphics;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.SmoothingMode = SmoothingMode.None;

            // Draw exactly one physical label — size from app mm settings, not the (often wrong) page bounds.
            var prevUnit = g.PageUnit;
            g.PageUnit = System.Drawing.GraphicsUnit.Inch;
            var wIn = widthMm / 25.4f;
            var hIn = heightMm / 25.4f;
            g.DrawImage(label, 0f, 0f, wIn, hIn);
            g.PageUnit = prevUnit;

            e.HasMorePages = false;
        };

        try
        {
            document.Print();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyLabelPaperSize(PrintDocument document, int widthMm, int heightMm)
    {
        var targetW = MmToHundredthsOfInch(widthMm);
        var targetH = MmToHundredthsOfInch(heightMm);

        var best = FindClosestPaperSize(document.PrinterSettings, targetW, targetH);
        var paper = best ?? new PaperSize("StoreLabel", targetW, targetH);

        document.DefaultPageSettings.PaperSize = paper;
        document.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
        document.DefaultPageSettings.Landscape = false;
        document.PrinterSettings.DefaultPageSettings.PaperSize = paper;
        document.PrinterSettings.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
        document.PrinterSettings.DefaultPageSettings.Landscape = false;
    }

    private static PaperSize? FindClosestPaperSize(PrinterSettings settings, int targetW, int targetH)
    {
        PaperSize? best = null;
        long bestScore = long.MaxValue;
        foreach (PaperSize size in settings.PaperSizes)
        {
            if (size.Width <= 0 || size.Height <= 0)
                continue;

            var dw = size.Width - targetW;
            var dh = size.Height - targetH;
            var score = (long)dw * dw + (long)dh * dh;

            var dwSwap = size.Width - targetH;
            var dhSwap = size.Height - targetW;
            var scoreSwap = (long)dwSwap * dwSwap + (long)dhSwap * dhSwap;
            score = Math.Min(score, scoreSwap);

            if (score < bestScore)
            {
                bestScore = score;
                best = size;
            }
        }

        const int toleranceHundredths = 80;
        return bestScore <= (long)toleranceHundredths * toleranceHundredths ? best : null;
    }

    private static int MmToHundredthsOfInch(int mm) =>
        (int)Math.Round(mm / 25.4 * 100);

    private static int MmToPixels(int mm, int dpi) =>
        (int)Math.Round(mm / 25.4 * dpi);

    private static double HundredthsToMm(int hundredths) =>
        hundredths / 100.0 * 25.4;

    /// <summary>Full-label corner marks — same pixel dimensions as real print jobs.</summary>
    private static DrawingBitmap RenderCalibrationBitmap(int widthMm, int heightMm)
    {
        var pixW = Math.Max(1, MmToPixels(widthMm, LabelDpi));
        var pixH = Math.Max(1, MmToPixels(heightMm, LabelDpi));
        var arm = Math.Max(10, Math.Min(pixW, pixH) / 7);
        var penWidth = Math.Max(2f, Math.Min(pixW, pixH) / 50f);

        var bmp = new DrawingBitmap(pixW, pixH, PixelFormat.Format24bppRgb);
        bmp.SetResolution(LabelDpi, LabelDpi);

        using (var g = DrawingGraphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.White);
            g.SmoothingMode = SmoothingMode.None;
            using var pen = new DrawingPen(System.Drawing.Color.Black, penWidth);

            DrawCornerL(g, pen, 0, 0, arm, true, true);
            DrawCornerL(g, pen, pixW - 1, 0, arm, false, true);
            DrawCornerL(g, pen, 0, pixH - 1, arm, true, false);
            DrawCornerL(g, pen, pixW - 1, pixH - 1, arm, false, false);

            using var font = new System.Drawing.Font("Arial", Math.Max(6f, pixH / 14f), System.Drawing.FontStyle.Bold);
            var text = $"{widthMm}×{heightMm} mm";
            var size = g.MeasureString(text, font);
            g.DrawString(
                text,
                font,
                System.Drawing.Brushes.Black,
                (pixW - size.Width) / 2f,
                (pixH - size.Height) / 2f);
        }

        return bmp;
    }

    private static void DrawCornerL(
        DrawingGraphics g,
        DrawingPen pen,
        int cornerX,
        int cornerY,
        int arm,
        bool fromLeft,
        bool fromTop)
    {
        var hx = fromLeft ? cornerX + arm : cornerX - arm;
        var vy = fromTop ? cornerY + arm : cornerY - arm;
        g.DrawLine(pen, cornerX, cornerY, hx, cornerY);
        g.DrawLine(pen, cornerX, cornerY, cornerX, vy);
    }

    private static string BitmapToPngBase64(DrawingBitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return Convert.ToBase64String(ms.ToArray());
    }
}
