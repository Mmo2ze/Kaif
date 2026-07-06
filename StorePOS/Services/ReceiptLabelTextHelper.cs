using SkiaSharp;
using StoreShared.Text;

namespace StorePOS.Services;

/// <summary>HarfBuzz-shaped text for Arabic receipt item lines.</summary>
internal static class ReceiptLabelTextHelper
{
    public static bool ContainsArabic(string text) => LabelFontResolver.ContainsArabic(text);

    public static SKTypeface ResolveTypeface(string text, SKFontStyle style) =>
        LabelFontResolver.ResolveTypeface(text, style);

    public static SKPaint CreatePaint(string text, SKFontStyle style, float textSizePx)
    {
        return new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true,
            TextSize = textSizePx,
            Typeface = ResolveTypeface(text, style),
        };
    }

    public static float MeasureTextWidth(string text, SKFontStyle style, float textSizePx)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        if (ContainsArabic(text) && BidiTextRuns.HasMixedScripts(text))
            return BidiTextRuns.MeasureRunsWidth(BidiTextRuns.Split(text), style, textSizePx);

        if (ContainsArabic(text))
        {
            using var paint = CreatePaint(text, style, textSizePx);
            return ArabicTextShaper.MeasureWidth(text, paint);
        }

        using var latinPaint = CreatePaint(text, style, textSizePx);
        return latinPaint.MeasureText(text);
    }

    public static void DrawItemLine(
        SKCanvas canvas,
        string left,
        string right,
        float textSizePx,
        float leftIndent,
        float lineWidth,
        float baselineY)
    {
        if (!string.IsNullOrEmpty(left))
            DrawLinePart(canvas, left, leftIndent, baselineY, SKFontStyle.Normal, textSizePx);

        if (string.IsNullOrEmpty(right))
            return;

        var rightWidth = MeasureTextWidth(right, SKFontStyle.Normal, textSizePx);
        DrawLinePart(canvas, right, lineWidth - rightWidth, baselineY, SKFontStyle.Normal, textSizePx);
    }

    public static float LineHeight(float textSizePx) => textSizePx * 1.25f + 2f;

    public static void DrawCentered(
        SKCanvas canvas,
        string text,
        float baselineY,
        float lineWidth,
        SKFontStyle style,
        float textSizePx)
    {
        if (string.IsNullOrEmpty(text))
            return;

        if (ContainsArabic(text) && BidiTextRuns.HasMixedScripts(text))
        {
            BidiTextRuns.DrawRunsCentered(canvas, BidiTextRuns.Split(text), baselineY, style, textSizePx, lineWidth);
            return;
        }

        if (ContainsArabic(text))
        {
            using var paint = CreatePaint(text, style, textSizePx);
            ArabicTextShaper.Draw(canvas, text, baselineY, paint, ArabicTextShaper.HorizontalAlign.Center, lineWidth);
            return;
        }

        using var latinPaint = CreatePaint(text, style, textSizePx);
        var width = latinPaint.MeasureText(text);
        canvas.DrawText(text, (lineWidth - width) / 2f, baselineY, latinPaint);
    }

    private static void DrawLinePart(
        SKCanvas canvas,
        string text,
        float x,
        float baselineY,
        SKFontStyle style,
        float textSizePx)
    {
        if (ContainsArabic(text) && BidiTextRuns.HasMixedScripts(text))
        {
            BidiTextRuns.DrawRunsLeft(canvas, BidiTextRuns.Split(text), x, baselineY, style, textSizePx);
            return;
        }

        if (ContainsArabic(text))
        {
            using var paint = CreatePaint(text, style, textSizePx);
            ArabicTextShaper.DrawAt(canvas, text, x, baselineY, paint);
            return;
        }

        using var latinPaint = CreatePaint(text, style, textSizePx);
        canvas.DrawText(text, x, baselineY, latinPaint);
    }
}
