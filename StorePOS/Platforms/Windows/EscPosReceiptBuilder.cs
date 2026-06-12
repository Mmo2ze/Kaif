using System.Drawing;
using System.Drawing.Text;
using System.Text;
using SkiaSharp;

using StoreShared.Receipt;

using StoreShared.Sales;



namespace StorePOS.Platforms.Windows;



public static class EscPosReceiptBuilder

{

    private const int LineWidth = 42;

    private const int NameColWidth = 28;

    private const int PriceColWidth = 14;

    private const byte BarcodeHeight = 60;

    private const byte Code128Auto = 73;



    private static readonly Encoding Enc;



    static EscPosReceiptBuilder()

    {

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        Enc = Encoding.GetEncoding(437);

    }



    private static readonly byte[] Init = [0x1B, 0x40];

    private static readonly byte[] AlignLeft = [0x1B, 0x61, 0x00];

    private static readonly byte[] AlignCenter = [0x1B, 0x61, 0x01];

    private static readonly byte[] AlignRight = [0x1B, 0x61, 0x02];

    private static readonly byte[] FontNormal = [0x1B, 0x21, 0x00];

    private static readonly byte[] FontDouble = [0x1B, 0x21, 0x30];

    private static readonly byte[] BoldOn = [0x1B, 0x45, 0x01];

    private static readonly byte[] BoldOff = [0x1B, 0x45, 0x00];

    private static readonly byte[] FontB = [0x1B, 0x4D, 0x01];

    private static readonly byte[] FontA = [0x1B, 0x4D, 0x00];

    private static readonly byte[] PartialCut = [0x1D, 0x56, 0x42, 0x03];



    public static byte[] BuildReceipt(ReceiptDto receipt, byte[]? logoRaster = null, string? cashier = null)

    {

        using var ms = new MemoryStream();

        var store = string.IsNullOrWhiteSpace(receipt.StoreName) ? "Kaif" : receipt.StoreName.Trim();



        W(ms, Init);

        PrintLogo(ms, logoRaster);



        // Store name header only when there is no logo.

        if (logoRaster is null || logoRaster.Length == 0)

        {

            W(ms, AlignCenter);

            W(ms, FontDouble);

            W(ms, BoldOn);

            T(ms, store);

            Lf(ms);

            W(ms, BoldOff);

            W(ms, FontNormal);

            Feed(ms, 1);

        }



        if (!string.IsNullOrWhiteSpace(receipt.StoreAddress))

        {

            W(ms, AlignCenter);

            T(ms, Truncate(receipt.StoreAddress.Trim(), LineWidth));

            Lf(ms);

            Feed(ms, 1);

        }



        Separator(ms, '-');

        W(ms, AlignLeft);

        T(ms, $"Date : {receipt.Date:dd/MM/yyyy HH:mm}");

        Lf(ms);

        T(ms, $"Ref# : {receipt.ReceiptNumber}");

        Lf(ms);

        if (!string.IsNullOrWhiteSpace(cashier))

        {

            T(ms, $"Cashier : {Truncate(cashier.Trim(), LineWidth - 10)}");

            Lf(ms);

        }

        Separator(ms, '-');



        W(ms, AlignLeft);

        W(ms, FontA);

        if (receipt.Lines.Count == 0)

        {

            W(ms, AlignCenter);

            T(ms, "No items");

            Lf(ms);

        }

        else

        {

            foreach (var line in receipt.Lines)

                WriteItemLines(ms, line);

        }



        Separator(ms, '-');



        W(ms, AlignLeft);

        T(ms, TotalsLine("Subtotal", $"{receipt.Subtotal:0.00} EGP"));

        Lf(ms);

        if (receipt.Discount > 0)

        {

            T(ms, TotalsLine("Discount", $"-{receipt.Discount:0.00} EGP"));

            Lf(ms);

        }



        if (receipt.Tax > 0)

        {

            T(ms, TotalsLine("Tax", $"{receipt.Tax:0.00} EGP"));

            Lf(ms);

        }



        Separator(ms, '=');

        W(ms, AlignCenter);

        W(ms, FontDouble);

        W(ms, BoldOn);

        T(ms, $"TOTAL  {receipt.Total:0.00} EGP");

        Lf(ms);

        W(ms, BoldOff);

        W(ms, FontNormal);

        Feed(ms, 1);



        PrintReceiptIdBarcode(ms, receipt.ReceiptNumber);



        Separator(ms, '-');

        PrintReceiptFooter(ms, store, receipt.ReceiptLandline, receipt.ReceiptPhone);



        W(ms, PartialCut);



        return ms.ToArray();

    }



    public static byte[] BuildRefundReceipt(RefundReceiptDto refund, byte[]? logoRaster = null)

    {

        using var ms = new MemoryStream();

        var store = string.IsNullOrWhiteSpace(refund.StoreName) ? "Kaif" : refund.StoreName.Trim();



        W(ms, Init);

        PrintLogo(ms, logoRaster);

        W(ms, AlignCenter);

        if (logoRaster is null || logoRaster.Length == 0)

        {

            W(ms, FontDouble);

            W(ms, BoldOn);

            T(ms, store);

            Lf(ms);

            W(ms, FontNormal);

        }

        W(ms, BoldOn);

        T(ms, "** REFUND RECEIPT **");

        Lf(ms);

        W(ms, BoldOff);

        Feed(ms, 1);



        Separator(ms, '-');



        W(ms, AlignLeft);

        T(ms, $"Date     : {refund.Timestamp:dd/MM/yyyy HH:mm}");

        Lf(ms);

        T(ms, $"Refund # : {refund.RefundReceiptNumber}");

        Lf(ms);

        T(ms, $"Orig Ref : {refund.OriginalReceiptNumber}");

        Lf(ms);

        T(ms, $"Type     : {refund.TypeLabel}");

        Lf(ms);

        T(ms, $"By       : {refund.PerformedBy}");

        Lf(ms);

        if (!string.IsNullOrWhiteSpace(refund.Reason))

        {

            T(ms, $"Reason   : {Truncate(refund.Reason.Trim(), LineWidth - 10)}");

            Lf(ms);

        }



        Separator(ms, '-');



        W(ms, AlignLeft);

        foreach (var line in refund.Lines)

            WriteItemLines(ms, line.ProductName, line.Size, line.Quantity, line.LineTotal, "(-) ");



        Separator(ms, '-');



        W(ms, AlignCenter);

        W(ms, BoldOn);

        T(ms, $"Amount Refunded : {refund.AmountRefunded:0.00} EGP");

        Lf(ms);

        W(ms, BoldOff);

        Feed(ms, 1);



        W(ms, AlignCenter);

        W(ms, BoldOn);

        T(ms, $"Refund ID: {refund.RefundReceiptNumber}");

        Lf(ms);

        W(ms, BoldOff);

        PrintCode128(ms, refund.RefundReceiptNumber);

        Feed(ms, 1);



        Separator(ms, '-');

        W(ms, AlignCenter);

        T(ms, "This refund has been processed.");

        Lf(ms);

        T(ms, "Please keep this receipt.");

        Lf(ms);

        PrintContactLines(ms, refund.ReceiptLandline, refund.ReceiptPhone);

        PrintRefundPolicyFooter(ms);

        Feed(ms, 3);



        W(ms, PartialCut);

        return ms.ToArray();

    }



    private static void PrintLogo(MemoryStream ms, byte[]? logoRaster)
    {
        if (logoRaster is null || logoRaster.Length == 0)
            return;

        W(ms, AlignCenter);
        W(ms, logoRaster);
        Lf(ms);
        Feed(ms, 1);
    }



    /// <summary>Max raster width in printer dots (80 mm head is 576; keep margins).</summary>
    private const int LogoMaxWidthDots = 384;

    /// <summary>Match 42-column ESC/POS text (~12 dots per character).</summary>
    private const int ReceiptTextRasterWidthDots = 504;

    private const float ReceiptItemFontPt = 15f;

    private const float ReceiptFooterFontPt = 11f;

    private const string RefundPolicyAr =
        "يحق للعميل الاسترجاع أو الاستبدال خلال 7 أيام بشرط وجود الفاتورة وأن يكون المنتج بحالته الأصلية";

    private const int LogoMaxHeightDots = 240;



    /// <summary>
    /// Converts an image file to an ESC/POS GS v 0 raster block (black/white, scaled to fit).
    /// Returns null if the file is missing or unreadable.
    /// </summary>
    public static byte[]? BuildLogoRaster(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            using var src = System.Drawing.Image.FromFile(path);

            var scale = Math.Min(1.0, Math.Min(
                (double)LogoMaxWidthDots / src.Width,
                (double)LogoMaxHeightDots / src.Height));
            var w = Math.Max(1, (int)Math.Round(src.Width * scale));
            var h = Math.Max(1, (int)Math.Round(src.Height * scale));

            using var bmp = new System.Drawing.Bitmap(w, h);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.White);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, w, h);
            }

            return BitmapToEscPosRaster(bmp);
        }
        catch
        {
            return null;
        }
    }



    private static void PrintReceiptFooter(
        MemoryStream ms,
        string storeName,
        string? landline,
        string? phone)
    {
        W(ms, AlignCenter);

        T(ms, $"Thank you for shopping at {Truncate(storeName, LineWidth)}!");

        Lf(ms);

        Feed(ms, 1);

        PrintContactLines(ms, landline, phone);

        PrintRefundPolicyFooter(ms);

        Feed(ms, 3);
    }



    /// <summary>Label left, value right, padded across the full line width.</summary>
    private static string TotalsLine(string label, string value)
    {
        var pad = LineWidth - label.Length - value.Length;
        return pad > 0 ? label + new string(' ', pad) + value : Truncate(label + " " + value, LineWidth);
    }



    private static void PrintRefundPolicyFooter(MemoryStream ms)

    {

        var fontPx = ReceiptPtToPx(ReceiptFooterFontPt);

        var maxWidth = ReceiptTextRasterWidthDots - 8f;

        var lines = WrapAtWordBoundaries(RefundPolicyAr, fontPx, maxWidth, maxWidth);

        if (lines.Count == 0)

            return;



        W(ms, AlignCenter);

        Feed(ms, 1);



        foreach (var line in lines)

        {

            var raster = BuildCenteredTextLineRaster(line, fontPx);

            if (raster is null || raster.Length == 0)

                continue;



            W(ms, AlignLeft);

            W(ms, raster);

            Lf(ms);

        }

    }



    private static byte[]? BuildCenteredTextLineRaster(string text, float fontPx)

    {

        var lineHeight = (int)Math.Ceiling(ReceiptLabelTextHelper.LineHeight(fontPx));

        var baselineY = fontPx;



        using var surface = SKSurface.Create(new SKImageInfo(ReceiptTextRasterWidthDots, lineHeight));

        var canvas = surface.Canvas;

        canvas.Clear(SKColors.White);

        ReceiptLabelTextHelper.DrawCentered(

            canvas,

            text,

            baselineY,

            ReceiptTextRasterWidthDots,

            SKFontStyle.Normal,

            fontPx);



        using var image = surface.Snapshot();

        using var bitmap = SKBitmap.FromImage(image);

        return bitmap is null ? null : SkBitmapToEscPosRaster(bitmap);

    }



    private static void PrintContactLines(MemoryStream ms, string? landline, string? phone)
    {
        if (!string.IsNullOrWhiteSpace(landline))
        {
            W(ms, AlignCenter);

            T(ms, $"Landline : {Truncate(landline.Trim(), LineWidth - 11)}");

            Lf(ms);
        }

        if (!string.IsNullOrWhiteSpace(phone))
        {
            W(ms, AlignCenter);

            T(ms, $"Phone    : {Truncate(phone.Trim(), LineWidth - 11)}");

            Lf(ms);
        }

        if (!string.IsNullOrWhiteSpace(landline) || !string.IsNullOrWhiteSpace(phone))
            Feed(ms, 1);
    }



    private static void PrintReceiptIdBarcode(MemoryStream ms, string receiptNumber)

    {

        W(ms, AlignCenter);

        W(ms, BoldOn);

        T(ms, $"Receipt ID: {receiptNumber}");

        Lf(ms);

        W(ms, BoldOff);

        PrintCode128(ms, receiptNumber);

        Feed(ms, 1);

        W(ms, AlignLeft);

    }



    private static void PrintCode128(MemoryStream ms, string data)

    {

        var payload = Enc.GetBytes(data);

        if (payload.Length > 255)

            payload = payload.Take(255).ToArray();



        W(ms, AlignCenter);

        ms.WriteByte(0x1D);

        ms.WriteByte(0x68);

        ms.WriteByte(BarcodeHeight);

        ms.WriteByte(0x1D);

        ms.WriteByte(0x48);

        ms.WriteByte(0x00);

        ms.WriteByte(0x1D);

        ms.WriteByte(0x6B);

        ms.WriteByte(Code128Auto);

        ms.WriteByte((byte)payload.Length);

        ms.Write(payload, 0, payload.Length);

        Lf(ms);

    }



    private static void WriteItemLines(MemoryStream ms, ReceiptLineDto line) =>

        WriteItemLines(ms, line.ProductName, line.Size, line.Quantity, line.LineTotal, "");



    private static void WriteItemLines(

        MemoryStream ms,

        string productName,

        string size,

        int quantity,

        decimal lineTotal,

        string prefix)

    {

        var desc = $"{prefix}{quantity}x {productName} {size}".Trim();

        var price = $"{lineTotal:0.00} EGP";

        if (price.Length > PriceColWidth)

            price = price[^PriceColWidth..];



        if (ContainsArabic(productName) || ContainsArabic(size) || ContainsArabic(desc))

        {

            PrintItemLineRaster(ms, desc, price);

            return;

        }



        if (desc.Length <= NameColWidth)

        {

            T(ms, FormatItemLine(desc, price));

            Lf(ms);

            return;

        }



        var first = desc[..NameColWidth];

        T(ms, FormatItemLine(first, price));

        Lf(ms);



        var rest = desc[NameColWidth..].TrimStart();

        while (rest.Length > 0)

        {

            var chunk = rest.Length <= NameColWidth - 3

                ? rest

                : rest[..(NameColWidth - 3)];

            T(ms, new string(' ', 3) + chunk);

            Lf(ms);

            rest = rest.Length <= NameColWidth - 3 ? "" : rest[(NameColWidth - 3)..].TrimStart();

        }

    }



    private static bool ContainsArabic(string text)

    {

        foreach (var ch in text)

        {

            if (ch is >= '\u0600' and <= '\u06FF'

                or >= '\u0750' and <= '\u077F'

                or >= '\u08A0' and <= '\u08FF'

                or >= '\uFB50' and <= '\uFDFF'

                or >= '\uFE70' and <= '\uFEFF')

                return true;

        }



        return false;

    }



    private const float ReceiptItemWrapIndentDots = 12f;



    private static void PrintItemLineRaster(MemoryStream ms, string desc, string price)

    {

        foreach (var raster in BuildItemLineRasters(desc, price))

        {

            if (raster is null || raster.Length == 0)

                continue;



            W(ms, AlignLeft);

            W(ms, raster);

            Lf(ms);

        }

    }



    private static float ReceiptPtToPx(float pt) => pt * 96f / 72f;



    private static IReadOnlyList<byte[]> BuildItemLineRasters(string desc, string price)

    {

        try

        {

            var fontPx = ReceiptPtToPx(ReceiptItemFontPt);

            var priceWidth = ReceiptLabelTextHelper.MeasureTextWidth(price, SKFontStyle.Normal, fontPx);

            var firstLineWidth = ReceiptTextRasterWidthDots - priceWidth - 4f;

            var wrapLineWidth = ReceiptTextRasterWidthDots - ReceiptItemWrapIndentDots - 4f;



            var wrapped = ReceiptLabelTextHelper.MeasureTextWidth(desc, SKFontStyle.Normal, fontPx) <= firstLineWidth

                ? [desc]

                : WrapReceiptDesc(desc, fontPx, firstLineWidth, wrapLineWidth);



            if (wrapped.Count == 0)

                return [];



            return RenderWrappedLines(wrapped, price, fontPx);

        }

        catch

        {

            return [];

        }

    }



    private static List<byte[]> RenderWrappedLines(IReadOnlyList<string> lines, string price, float fontPx)

    {

        var rasters = new List<byte[]>(lines.Count);

        for (var i = 0; i < lines.Count; i++)

        {

            var indent = i == 0 ? 0f : ReceiptItemWrapIndentDots;

            var linePrice = i == 0 ? price : "";

            var raster = BuildSingleItemLineRaster(lines[i], linePrice, fontPx, indent);

            if (raster is not null && raster.Length > 0)

                rasters.Add(raster);

        }



        return rasters;

    }



    private static List<string> WrapReceiptDesc(

        string desc,

        float textSizePx,

        float firstLineWidth,

        float wrapLineWidth)

    {

        var words = desc.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length <= 1)

            return [desc];



        var last = words[^1];

        var keepSizeOnFirstLine = last.Length <= 4 && !ReceiptLabelTextHelper.ContainsArabic(last);

        if (!keepSizeOnFirstLine)

            return WrapAtWordBoundaries(desc, textSizePx, firstLineWidth, wrapLineWidth);



        var suffix = " " + last;

        var namePart = string.Join(' ', words[..^1]);

        return WrapNameWithSuffix(namePart, suffix, textSizePx, firstLineWidth, wrapLineWidth);

    }



    private static List<string> WrapNameWithSuffix(

        string namePart,

        string suffix,

        float textSizePx,

        float firstLineWidth,

        float wrapLineWidth)

    {

        var full = namePart + suffix;

        if (ReceiptLabelTextHelper.MeasureTextWidth(full, SKFontStyle.Normal, textSizePx) <= firstLineWidth)

            return [full];



        var words = namePart.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0)

            return [suffix.TrimStart()];



        var lines = new List<string>();

        var index = 0;

        var line1 = "";



        while (index < words.Length)

        {

            var candidate = string.IsNullOrEmpty(line1) ? words[index] : $"{line1} {words[index]}";

            if (ReceiptLabelTextHelper.MeasureTextWidth(candidate + suffix, SKFontStyle.Normal, textSizePx) <= firstLineWidth)

            {

                line1 = candidate;

                index++;

                continue;

            }



            break;

        }



        if (string.IsNullOrEmpty(line1))

        {

            line1 = words[index];

            index++;

        }



        lines.Add(line1 + suffix);



        var current = "";

        while (index < words.Length)

        {

            var candidate = string.IsNullOrEmpty(current) ? words[index] : $"{current} {words[index]}";

            if (ReceiptLabelTextHelper.MeasureTextWidth(candidate, SKFontStyle.Normal, textSizePx) <= wrapLineWidth)

            {

                current = candidate;

                index++;

                continue;

            }



            if (!string.IsNullOrEmpty(current))

            {

                lines.Add(current);

                current = "";

                continue;

            }



            lines.Add(words[index]);

            index++;

        }



        if (!string.IsNullOrEmpty(current))

            lines.Add(current);



        return lines;

    }



    private static List<string> WrapAtWordBoundaries(

        string text,

        float textSizePx,

        float firstLineWidth,

        float wrapLineWidth)

    {

        var lines = new List<string>();

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0)

            return lines;



        var current = "";

        var maxWidth = firstLineWidth;



        foreach (var word in words)

        {

            var candidate = string.IsNullOrEmpty(current) ? word : $"{current} {word}";

            if (ReceiptLabelTextHelper.MeasureTextWidth(candidate, SKFontStyle.Normal, textSizePx) <= maxWidth)

            {

                current = candidate;

                continue;

            }



            if (!string.IsNullOrEmpty(current))

                lines.Add(current);



            maxWidth = wrapLineWidth;

            current = word;

        }



        if (!string.IsNullOrEmpty(current))

            lines.Add(current);



        return lines;

    }



    private static byte[]? BuildSingleItemLineRaster(

        string left,

        string right,

        float textSizePx,

        float leftIndent)

    {

        var lineHeight = (int)Math.Ceiling(ReceiptLabelTextHelper.LineHeight(textSizePx));

        var baselineY = textSizePx;



        using var surface = SKSurface.Create(new SKImageInfo(ReceiptTextRasterWidthDots, lineHeight));

        var canvas = surface.Canvas;

        canvas.Clear(SKColors.White);

        ReceiptLabelTextHelper.DrawItemLine(

            canvas,

            left,

            right,

            textSizePx,

            leftIndent,

            ReceiptTextRasterWidthDots,

            baselineY);



        using var image = surface.Snapshot();

        using var bitmap = SKBitmap.FromImage(image);

        return bitmap is null ? null : SkBitmapToEscPosRaster(bitmap);

    }



    private static byte[] SkBitmapToEscPosRaster(SKBitmap bmp)

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



    private static byte[] BitmapToEscPosRaster(Bitmap bmp)

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

                var lum = 0.299 * p.R + 0.587 * p.G + 0.114 * p.B;

                if (p.A > 128 && lum < 160)

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



    private static string FormatItemLine(string left, string price) =>

        Truncate(left, NameColWidth).PadRight(NameColWidth) + price.PadLeft(PriceColWidth);



    private static void Separator(MemoryStream ms, char c)

    {

        W(ms, AlignLeft);

        T(ms, new string(c, LineWidth));

        Lf(ms);

    }



    private static string Truncate(string value, int max) =>

        value.Length <= max ? value : value[..max];



    private static void W(MemoryStream ms, byte[] bytes) => ms.Write(bytes, 0, bytes.Length);



    private static void T(MemoryStream ms, string text)

    {

        var bytes = Enc.GetBytes(text);

        ms.Write(bytes, 0, bytes.Length);

    }



    private static void Lf(MemoryStream ms) => ms.WriteByte(0x0A);



    private static void Feed(MemoryStream ms, int lines)

    {

        if (lines <= 0)

            return;

        ms.WriteByte(0x1B);

        ms.WriteByte(0x64);

        ms.WriteByte((byte)lines);

    }

}


