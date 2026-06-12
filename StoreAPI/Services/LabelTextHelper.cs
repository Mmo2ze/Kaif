using System.Text;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace StoreAPI.Services;

/// <summary>Draws label text with HarfBuzz shaping for connected Arabic script.</summary>
internal static class LabelTextHelper
{
    private static readonly string[] ArabicFontFiles = ["tahoma.ttf", "segoeui.ttf", "arial.ttf"];
    private static readonly string[] LatinFontFamilies = ["Segoe UI", "Arial"];

    private readonly record struct TextRun(string Text, bool IsArabic);

    public static bool ContainsArabic(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (IsArabicRune(rune))
                return true;
        }

        return false;
    }

    public static SKTypeface ResolveTypeface(string text, SKFontStyle style)
    {
        if (ContainsArabic(text))
        {
            var fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            foreach (var file in ArabicFontFiles)
            {
                var path = Path.Combine(fontsDir, file);
                if (!File.Exists(path))
                    continue;
                var fromFile = SKTypeface.FromFile(path);
                if (fromFile is not null)
                    return fromFile;
            }
        }

        foreach (var family in LatinFontFamilies)
        {
            var tf = SKTypeface.FromFamilyName(family, style);
            if (tf is not null && !string.IsNullOrEmpty(tf.FamilyName))
                return tf;
        }

        return SKTypeface.FromFamilyName("Arial", style)
               ?? SKTypeface.FromFamilyName("sans-serif", style)
               ?? SKTypeface.CreateDefault();
    }

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

        if (ContainsArabic(text) && HasMixedScripts(text))
        {
            var runs = SplitRuns(text);
            var totalWidth = runs.Sum(r => MeasureRun(r, style, textSize));
            DrawRuns(canvas, runs, (labelWidth - totalWidth) / 2f, baselineY, style, textSize);
            return;
        }

        if (ContainsArabic(text))
        {
            DrawShaped(canvas, text, 0f, baselineY, SKTextAlign.Center, labelWidth, style, textSize);
            return;
        }

        using var paint = CreatePaint(text, style, textSize);
        var width = paint.MeasureText(text);
        canvas.DrawText(text, (labelWidth - width) / 2f, baselineY, paint);
    }

    public static float MeasureTextWidth(string text, SKFontStyle style, float textSize)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        if (ContainsArabic(text) && HasMixedScripts(text))
            return SplitRuns(text).Sum(r => MeasureRun(r, style, textSize));

        if (ContainsArabic(text))
            return MeasureShapedWidth(text, style, textSize);

        using var paint = CreatePaint(text, style, textSize);
        return paint.MeasureText(text);
    }

    private static void DrawShaped(
        SKCanvas canvas,
        string text,
        float x,
        float baselineY,
        SKTextAlign align,
        float lineWidth,
        SKFontStyle style,
        float textSize)
    {
        using var paint = CreatePaint(text, style, textSize);
        using var shaper = new SKShaper(paint.Typeface);
        var width = shaper.Shape(text, paint).Width;
        var drawX = align == SKTextAlign.Center
            ? (lineWidth - width) / 2f
            : x;
        canvas.DrawShapedText(shaper, text, drawX, baselineY, paint);
    }

    private static float MeasureShapedWidth(string text, SKFontStyle style, float textSize)
    {
        using var paint = CreatePaint(text, style, textSize);
        using var shaper = new SKShaper(paint.Typeface);
        return shaper.Shape(text, paint).Width;
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

    private static void DrawRuns(SKCanvas canvas, IReadOnlyList<TextRun> runs, float x, float baselineY, SKFontStyle style, float textSize)
    {
        foreach (var run in runs)
        {
            if (string.IsNullOrEmpty(run.Text))
                continue;

            using var paint = CreatePaint(run.Text, style, textSize);
            if (run.IsArabic)
            {
                using var shaper = new SKShaper(paint.Typeface);
                canvas.DrawShapedText(shaper, run.Text, x, baselineY, paint);
                x += shaper.Shape(run.Text, paint).Width;
            }
            else
            {
                canvas.DrawText(run.Text, x, baselineY, paint);
                x += paint.MeasureText(run.Text);
            }
        }
    }

    private static float MeasureRun(TextRun run, SKFontStyle style, float textSize)
    {
        if (string.IsNullOrEmpty(run.Text))
            return 0;

        using var paint = CreatePaint(run.Text, style, textSize);
        if (run.IsArabic)
        {
            using var shaper = new SKShaper(paint.Typeface);
            return shaper.Shape(run.Text, paint).Width;
        }

        return paint.MeasureText(run.Text);
    }

    private static List<TextRun> SplitRuns(string text)
    {
        var runs = new List<TextRun>();
        if (string.IsNullOrEmpty(text))
            return runs;

        var sb = new StringBuilder();
        bool? currentArabic = null;

        foreach (var rune in text.EnumerateRunes())
        {
            var isArabic = IsArabicRune(rune);
            if (currentArabic.HasValue && currentArabic != isArabic)
            {
                runs.Add(new TextRun(sb.ToString(), currentArabic.Value));
                sb.Clear();
            }

            currentArabic = isArabic;
            sb.Append(rune);
        }

        if (sb.Length > 0)
            runs.Add(new TextRun(sb.ToString(), currentArabic ?? false));

        return runs;
    }

    private static bool IsArabicRune(Rune rune)
    {
        var v = rune.Value;
        return v is >= '\u0600' and <= '\u06FF' or >= '\u0750' and <= '\u077F' or >= '\u08A0' and <= '\u08FF';
    }

    /// <summary>Arabic product name plus Latin size suffix (e.g. " - xxl") must not be shaped as one RTL string.</summary>
    private static bool HasMixedScripts(string text)
    {
        var hasArabic = false;
        var hasNonArabic = false;

        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
                continue;

            if (IsArabicRune(rune))
                hasArabic = true;
            else
                hasNonArabic = true;

            if (hasArabic && hasNonArabic)
                return true;
        }

        return false;
    }
}
