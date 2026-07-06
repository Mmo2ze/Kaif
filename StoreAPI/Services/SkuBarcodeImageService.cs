using System.Collections.Concurrent;
using BarcodeStandard;
using SkiaSharp;
using StoreShared;
using StoreShared.Barcode;

namespace StoreAPI.Services;

public sealed class SkuBarcodeImageService
{
    private const int LabelW = LabelDimensions.WidthPx;
    private const int LabelH = LabelDimensions.HeightPx;
    private const float Margin = 4f;
    private const string LabelStoreName = "Kaif";
    private const float StoreTopMargin = 3f;
    private const float PriceBottomMargin = 3f;
    private static readonly float StoreFontPx = PtToPx(14f);
    private static readonly float ProductFontPx = PtToPx(9f);
    private static readonly float ProductFontMinPx = PtToPx(5f);
    private static readonly float PriceFontPx = PtToPx(11f);
    private static readonly float OriginalPriceFontPx = PtToPx(9f);
    private static readonly float BarcodeDigitFontPx = PtToPx(8f);
    private const float BarcodeMaxHeightPx = 68f;

    private static float PtToPx(float pt) => pt * LabelDimensions.PrintDpi / 72f;

    private readonly ConcurrentDictionary<string, byte[]> _pngBytes = new(StringComparer.Ordinal);

    public string ToPngBase64(string barcodeText, BarcodeImageKind kind = BarcodeImageKind.Standard)
        => Convert.ToBase64String(ToPngBytes(barcodeText, kind));

    public byte[] ToPngBytes(string barcodeText, BarcodeImageKind kind = BarcodeImageKind.Standard)
    {
        if (string.IsNullOrWhiteSpace(barcodeText))
            throw new ArgumentException("Barcode text is required.", nameof(barcodeText));

        var article7 = SkuBarcode.GetArticle7(barcodeText.Trim());
        var cacheKey = ((char)kind) + article7;
        return _pngBytes.GetOrAdd(cacheKey, static k =>
        {
            var kind = (BarcodeImageKind)k[0];
            var article = k[1..];
            return EncodeEan8Png(article, kind);
        });
    }

    public string ToFullLabelPngBase64(SkuLabelContent content)
    {
        var cacheKey = string.Join('\x1f', StoreBuild.LabelRenderVersion, content.Barcode, content.ProductName, content.PriceText, content.OriginalPriceText ?? "");
        return Convert.ToBase64String(_pngBytes.GetOrAdd(cacheKey, static k =>
        {
            var p = k.Split('\x1f', 5);
            var label = new SkuLabelContent(LabelStoreName, p[2], p[3], p[1], p[4].Length == 0 ? null : p[4]);
            return ComposeFullLabel(label);
        }));
    }

    public void ClearCache() => _pngBytes.Clear();

    private static byte[] ComposeFullLabel(SkuLabelContent content)
    {
        using var surface = SKSurface.Create(new SKImageInfo(LabelW, LabelH));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        var innerW = LabelW - Margin * 2f;

        // 1. Store name — always Kaif on printed labels
        var y = Margin + StoreTopMargin + StoreFontPx;
        LabelTextHelper.DrawCentered(canvas, LabelStoreName, y, LabelW, SKFontStyle.Normal, StoreFontPx);

        // 2. Product name (shrink to fit, else up to 2 lines; HarfBuzz for Arabic)
        var productAreaTop = y + ProductFontPx * 0.35f;
        var priceTop = LabelH - Margin - PriceBottomMargin - PriceFontPx;
        var maxProductBlockH = priceTop - productAreaTop - 8f;
        var productPlan = LabelTextHelper.PlanProductText(
            content.ProductName,
            SKFontStyle.Normal,
            ProductFontPx,
            ProductFontMinPx,
            innerW,
            maxProductBlockH);

        var productBaseline = productAreaTop + productPlan.FontSizePx;
        foreach (var line in productPlan.Lines)
        {
            LabelTextHelper.DrawCentered(canvas, line, productBaseline, LabelW, SKFontStyle.Normal, productPlan.FontSizePx);
            productBaseline += LabelTextHelper.LineHeight(productPlan.FontSizePx);
        }

        // 3. EAN-8 (uniform scale, max ~70px tall, digits under bars from encoder)
        var article7 = SkuBarcode.GetArticle7(content.Barcode);
        var productBottom = productAreaTop + productPlan.BlockHeight + 4f;
        var barcodeSlotH = priceTop - productBottom - 4f;
        var slotH = Math.Min(BarcodeMaxHeightPx, barcodeSlotH);

        var barcodeBytes = EncodeEan8LabelPng(article7);
        using var barcodeImage = SKImage.FromEncodedData(barcodeBytes);
        if (barcodeImage is not null)
        {
            var scale = Math.Min(innerW / barcodeImage.Width, slotH / barcodeImage.Height);
            var dw = barcodeImage.Width * scale;
            var dh = barcodeImage.Height * scale;
            var dx = (LabelW - dw) / 2f;
            var dy = productBottom + (barcodeSlotH - dh) / 2f;
            canvas.DrawImage(barcodeImage, SKRect.Create(dx, dy, dw, dh));
        }

        // 4. Price at bottom — when on sale: tiny crossed-out original + normal sale price
        var priceBaseline = LabelH - Margin - PriceBottomMargin;
        if (string.IsNullOrEmpty(content.OriginalPriceText))
        {
            LabelTextHelper.DrawCentered(canvas, content.PriceText, priceBaseline, LabelW, SKFontStyle.Normal, PriceFontPx);
        }
        else
        {
            const float gap = 8f;
            var originalW = LabelTextHelper.MeasureTextWidth(content.OriginalPriceText, SKFontStyle.Normal, OriginalPriceFontPx);
            var saleW = LabelTextHelper.MeasureTextWidth(content.PriceText, SKFontStyle.Normal, PriceFontPx);
            var startX = Math.Max(Margin, (LabelW - (originalW + gap + saleW)) / 2f);

            using (var originalPaint = LabelTextHelper.CreatePaint(content.OriginalPriceText, SKFontStyle.Normal, OriginalPriceFontPx))
            {
                originalPaint.GetFontMetrics(out var originalMetrics);
                using var saleMetricsPaint = LabelTextHelper.CreatePaint(content.PriceText, SKFontStyle.Normal, PriceFontPx);
                saleMetricsPaint.GetFontMetrics(out var saleMetrics);
                var originalBaseline = priceBaseline - (saleMetrics.CapHeight - originalMetrics.CapHeight) / 2f;

                canvas.DrawText(content.OriginalPriceText, startX, originalBaseline, originalPaint);
                var pad = 1f;
                var slashX1 = startX - pad;
                var slashX2 = startX + originalW + pad;
                var slashY1 = originalBaseline + originalMetrics.Descent + pad * 0.5f;
                var slashY2 = originalBaseline + originalMetrics.Ascent - pad * 0.5f;

                using var strikePaint = new SKPaint
                {
                    Color = SKColors.Black,
                    IsAntialias = true,
                    StrokeWidth = Math.Max(1f, OriginalPriceFontPx / 12f),
                    StrokeCap = SKStrokeCap.Round,
                };
                canvas.DrawLine(slashX1, slashY1, slashX2, slashY2, strikePaint);
            }

            using (var salePaint = LabelTextHelper.CreatePaint(content.PriceText, SKFontStyle.Normal, PriceFontPx))
            {
                canvas.DrawText(content.PriceText, startX + originalW + gap, priceBaseline, salePaint);
            }
        }

        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte[] EncodeEan8LabelPng(string sevenDigits)
    {
        using var barcode = new Barcode
        {
            IncludeLabel = true,
            BarWidth = 3,
            Height = 52,
            Alignment = AlignmentPositions.Center,
            LabelFont = new SKFont
            {
                Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal),
                Size = BarcodeDigitFontPx,
            },
        };

        using var encoded = barcode.Encode(BarcodeStandard.Type.Ean8, sevenDigits, SKColors.Black, SKColors.White, 260, 68);
        using var data = encoded.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void DrawCenteredLine(SKCanvas canvas, string text, SKPaint paint, float baselineY, float labelWidth)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var x = (labelWidth - paint.MeasureText(text)) / 2f;
        canvas.DrawText(text, x, baselineY, paint);
    }

    private static SKPaint CreateTextPaint(SKFontStyle style, float textSize) =>
        new()
        {
            Color = SKColors.Black,
            IsAntialias = true,
            TextSize = textSize,
            Typeface = SKTypeface.FromFamilyName("Arial", style),
        };

    private static string TruncateToWidth(string text, SKPaint paint, float maxWidth)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        if (paint.MeasureText(text) <= maxWidth)
            return text;

        const string ellipsis = "…";
        var trimmed = text.Trim();
        while (trimmed.Length > 1)
        {
            trimmed = trimmed[..^1];
            if (paint.MeasureText(trimmed + ellipsis) <= maxWidth)
                return trimmed + ellipsis;
        }

        return ellipsis;
    }

    private static byte[] EncodeEan8Png(string sevenDigits, BarcodeImageKind kind)
    {
        var (barWidth, barHeight, padH, padV) = kind switch
        {
            BarcodeImageKind.Compact => (2, 56, 12, 6),
            BarcodeImageKind.Label => (3, 56, 8, 4),
            _ => (2, 64, 16, 8),
        };

        using var barcode = new Barcode
        {
            IncludeLabel = true,
            BarWidth = barWidth,
            Height = barHeight,
            Alignment = AlignmentPositions.Center,
            LabelFont = new SKFont
            {
                Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal),
                Size = 8,
            },
        };

        using var encoded = barcode.Encode(BarcodeStandard.Type.Ean8, sevenDigits, SKColors.Black, SKColors.White, 240, barHeight + 16);
        return AddQuietZone(encoded, padH, padV);
    }

    private static byte[] AddQuietZone(SKImage image, int padHorizontal, int padVertical)
    {
        var w = image.Width + padHorizontal * 2;
        var h = image.Height + padVertical * 2;
        using var surface = SKSurface.Create(new SKImageInfo(w, h));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.DrawImage(image, padHorizontal, padVertical);
        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
