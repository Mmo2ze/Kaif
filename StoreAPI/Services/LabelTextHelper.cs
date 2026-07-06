using System.Text;
using SkiaSharp;
using StoreShared.Text;

namespace StoreAPI.Services;

/// <summary>Draws label text with HarfBuzz shaping for connected Arabic script.</summary>
internal static class LabelTextHelper
{
    public static bool ContainsArabic(string text) => LabelFontResolver.ContainsArabic(text);

    public static SKTypeface ResolveTypeface(string text, SKFontStyle style) =>
        LabelFontResolver.ResolveTypeface(text, style);

    public static SKPaint CreatePaint(string text, SKFontStyle style, float textSize)
    {
        return new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true,
            TextSize = textSize,
            Typeface = ResolveTypeface(text, style),
        };
    }

    public static void DrawCentered(SKCanvas canvas, string text, float baselineY, float labelWidth, SKFontStyle style, float textSize)
    {
        if (string.IsNullOrEmpty(text))
            return;

        if (ContainsArabic(text) && BidiTextRuns.HasMixedScripts(text))
        {
            BidiTextRuns.DrawRunsCentered(canvas, BidiTextRuns.Split(text), baselineY, style, textSize, labelWidth);
            return;
        }

        if (ContainsArabic(text))
        {
            using var paint = CreatePaint(text, style, textSize);
            ArabicTextShaper.Draw(canvas, text, baselineY, paint, ArabicTextShaper.HorizontalAlign.Center, labelWidth);
            return;
        }

        using var latinPaint = CreatePaint(text, style, textSize);
        var width = latinPaint.MeasureText(text);
        canvas.DrawText(text, (labelWidth - width) / 2f, baselineY, latinPaint);
    }

    public static float MeasureTextWidth(string text, SKFontStyle style, float textSize)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        if (ContainsArabic(text) && BidiTextRuns.HasMixedScripts(text))
            return BidiTextRuns.MeasureRunsWidth(BidiTextRuns.Split(text), style, textSize);

        if (ContainsArabic(text))
        {
            using var paint = CreatePaint(text, style, textSize);
            return ArabicTextShaper.MeasureWidth(text, paint);
        }

        using var latinPaint = CreatePaint(text, style, textSize);
        return latinPaint.MeasureText(text);
    }

    public readonly record struct ProductTextPlan(IReadOnlyList<string> Lines, float FontSizePx, float BlockHeight);

    public static float LineHeight(float textSize) => textSize * 1.15f;

    public static float BlockHeightFor(int lineCount, float fontSize) =>
        lineCount <= 1
            ? fontSize * 1.1f
            : fontSize * 1.1f + LineHeight(fontSize) * (lineCount - 1);

    /// <summary>Prefer one line (shrink if needed), else normal font on up to two wrapped lines.</summary>
    public static ProductTextPlan PlanProductText(
        string text,
        SKFontStyle style,
        float normalSizePx,
        float minSizePx,
        float maxWidth,
        float maxBlockHeightPx,
        int maxLines = 2)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new ProductTextPlan([], normalSizePx, 0);

        if (MeasureTextWidth(text, style, normalSizePx) <= maxWidth)
            return new ProductTextPlan([text], normalSizePx, BlockHeightFor(1, normalSizePx));

        if (MeasureTextWidth(text, style, minSizePx) <= maxWidth)
        {
            var singleLineSize = FitTextSizeToWidth(text, style, normalSizePx, minSizePx, maxWidth);
            return new ProductTextPlan([text], singleLineSize, BlockHeightFor(1, singleLineSize));
        }

        var twoLines = WrapTwoLines(text, style, normalSizePx, maxWidth);
        return FinalizeMultiLinePlan(twoLines, style, normalSizePx, minSizePx, maxWidth, maxBlockHeightPx);
    }

    /// <summary>Product name with size suffix; keeps " - size" on line 1 when wrapping.</summary>
    public static ProductTextPlan PlanProductNameWithSize(
        string productName,
        string sizeText,
        SKFontStyle style,
        float normalSizePx,
        float minSizePx,
        float maxWidth,
        float maxBlockHeightPx)
    {
        var name = (productName ?? "").Trim();
        var size = (sizeText ?? "").Trim();
        if (string.IsNullOrEmpty(name))
            return PlanProductText(size, style, normalSizePx, minSizePx, maxWidth, maxBlockHeightPx);

        var suffix = string.IsNullOrEmpty(size) ? "" : $" - {size}";
        var full = name + suffix;

        if (MeasureTextWidth(full, style, normalSizePx) <= maxWidth)
            return new ProductTextPlan([full], normalSizePx, BlockHeightFor(1, normalSizePx));

        if (MeasureTextWidth(full, style, minSizePx) <= maxWidth)
        {
            var singleLineSize = FitTextSizeToWidth(full, style, normalSizePx, minSizePx, maxWidth);
            return new ProductTextPlan([full], singleLineSize, BlockHeightFor(1, singleLineSize));
        }

        var twoLines = WrapNameWithSuffix(name, suffix, style, normalSizePx, maxWidth);
        return FinalizeMultiLinePlan(twoLines, style, normalSizePx, minSizePx, maxWidth, maxBlockHeightPx);
    }

    private static ProductTextPlan FinalizeMultiLinePlan(
        IReadOnlyList<string> lines,
        SKFontStyle style,
        float normalSizePx,
        float minSizePx,
        float maxWidth,
        float maxBlockHeightPx)
    {
        var twoLineSize = FitFontForLines(lines, style, normalSizePx, minSizePx, maxWidth);
        var blockH = BlockHeightFor(lines.Count, twoLineSize);
        if (blockH <= maxBlockHeightPx)
            return new ProductTextPlan(lines, twoLineSize, blockH);

        twoLineSize = minSizePx;
        blockH = BlockHeightFor(lines.Count, twoLineSize);
        return new ProductTextPlan(lines, twoLineSize, blockH);
    }

    private static List<string> WrapNameWithSuffix(string name, string suffix, SKFontStyle style, float textSize, float maxWidth)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 1)
            return [name + suffix];

        var bestLines = new List<string> { name + suffix };
        var bestMax = MeasureTextWidth(name + suffix, style, textSize);
        for (var i = 1; i < words.Length; i++)
        {
            var line1 = string.Join(' ', words.Take(i)) + suffix;
            var line2 = string.Join(' ', words.Skip(i));
            if (string.IsNullOrEmpty(line2))
                continue;

            var maxLine = Math.Max(
                MeasureTextWidth(line1, style, textSize),
                MeasureTextWidth(line2, style, textSize));
            if (maxLine < bestMax)
            {
                bestMax = maxLine;
                bestLines = [line1, line2];
            }
        }

        return bestLines;
    }

    public static float FitTextSizeToWidth(
        string text,
        SKFontStyle style,
        float maxTextSize,
        float minTextSize,
        float maxWidth)
    {
        if (string.IsNullOrEmpty(text))
            return maxTextSize;

        if (MeasureTextWidth(text, style, maxTextSize) <= maxWidth)
            return maxTextSize;

        if (minTextSize > maxTextSize)
            (minTextSize, maxTextSize) = (maxTextSize, minTextSize);

        for (var size = maxTextSize; size >= minTextSize; size -= 0.5f)
        {
            if (MeasureTextWidth(text, style, size) <= maxWidth)
                return size;
        }

        return minTextSize;
    }

    public static string TruncateToWidth(string text, SKFontStyle style, float textSize, float maxWidth)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        if (MeasureTextWidth(text, style, textSize) <= maxWidth)
            return text;

        const string ellipsis = "…";
        var sb = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            sb.Append(rune);
            if (MeasureTextWidth(sb + ellipsis, style, textSize) > maxWidth)
            {
                sb.Length -= rune.Utf16SequenceLength;
                break;
            }
        }

        return sb.Length == 0 ? ellipsis : sb + ellipsis;
    }

    private static float FitFontForLines(
        IReadOnlyList<string> lines,
        SKFontStyle style,
        float maxSizePx,
        float minSizePx,
        float maxWidth)
    {
        for (var size = maxSizePx; size >= minSizePx; size -= 0.5f)
        {
            if (lines.All(line => MeasureTextWidth(line, style, size) <= maxWidth))
                return size;
        }

        return minSizePx;
    }

    private static List<string> WrapTwoLines(string text, SKFontStyle style, float textSize, float maxWidth)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 1)
            return [text];

        var bestSplit = 1;
        var bestMax = float.MaxValue;
        for (var i = 1; i < words.Length; i++)
        {
            var line1 = string.Join(' ', words.Take(i));
            var line2 = string.Join(' ', words.Skip(i));
            var maxLine = Math.Max(
                MeasureTextWidth(line1, style, textSize),
                MeasureTextWidth(line2, style, textSize));
            if (maxLine < bestMax)
            {
                bestMax = maxLine;
                bestSplit = i;
            }
        }

        var first = string.Join(' ', words.Take(bestSplit));
        var second = string.Join(' ', words.Skip(bestSplit));
        return string.IsNullOrEmpty(second) ? [first] : [first, second];
    }
}
