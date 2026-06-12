using System.Text;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace StorePOS.Platforms.Windows;

/// <summary>HarfBuzz-shaped text for Arabic receipt item lines.</summary>
internal static class ReceiptLabelTextHelper
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

        if (ContainsArabic(text) && HasMixedScripts(text))
            return SplitRuns(text).Sum(r => MeasureRun(r, style, textSizePx));

        if (ContainsArabic(text))
            return MeasureShapedWidth(text, style, textSizePx);

        using var paint = CreatePaint(text, style, textSizePx);
        return paint.MeasureText(text);
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
        {
            if (ContainsArabic(left) && HasMixedScripts(left))
                DrawRuns(canvas, SplitRuns(left), leftIndent, baselineY, SKFontStyle.Normal, textSizePx);
            else if (ContainsArabic(left))
                DrawShaped(canvas, left, leftIndent, baselineY, SKTextAlign.Left, lineWidth, SKFontStyle.Normal, textSizePx);
            else
            {
                using var paint = CreatePaint(left, SKFontStyle.Normal, textSizePx);
                canvas.DrawText(left, leftIndent, baselineY, paint);
            }
        }

        if (string.IsNullOrEmpty(right))
            return;

        var rightWidth = MeasureTextWidth(right, SKFontStyle.Normal, textSizePx);
        var rightX = lineWidth - rightWidth;
        if (ContainsArabic(right) && HasMixedScripts(right))
            DrawRuns(canvas, SplitRuns(right), rightX, baselineY, SKFontStyle.Normal, textSizePx);
        else if (ContainsArabic(right))
            DrawShaped(canvas, right, rightX, baselineY, SKTextAlign.Left, lineWidth, SKFontStyle.Normal, textSizePx);
        else
        {
            using var paint = CreatePaint(right, SKFontStyle.Normal, textSizePx);
            canvas.DrawText(right, rightX, baselineY, paint);
        }
    }

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

    private static void DrawShaped(
        SKCanvas canvas,
        string text,
        float x,
        float baselineY,
        SKTextAlign align,
        float lineWidth,
        SKFontStyle style,
        float textSizePx)
    {
        using var paint = CreatePaint(text, style, textSizePx);
        using var shaper = new SKShaper(paint.Typeface);
        var width = shaper.Shape(text, paint).Width;
        var drawX = align == SKTextAlign.Center
            ? (lineWidth - width) / 2f
            : x;
        canvas.DrawShapedText(shaper, text, drawX, baselineY, paint);
    }

    private static float MeasureShapedWidth(string text, SKFontStyle style, float textSizePx)
    {
        using var paint = CreatePaint(text, style, textSizePx);
        using var shaper = new SKShaper(paint.Typeface);
        return shaper.Shape(text, paint).Width;
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

        if (ContainsArabic(text))
            DrawShaped(canvas, text, 0f, baselineY, SKTextAlign.Center, lineWidth, style, textSizePx);
        else
        {
            using var paint = CreatePaint(text, style, textSizePx);
            var width = paint.MeasureText(text);
            canvas.DrawText(text, (lineWidth - width) / 2f, baselineY, paint);
        }
    }

    private static void DrawRuns(SKCanvas canvas, IReadOnlyList<TextRun> runs, float x, float baselineY, SKFontStyle style, float textSizePx)
    {
        foreach (var run in runs)
        {
            if (string.IsNullOrEmpty(run.Text))
                continue;

            if (run.IsArabic)
            {
                DrawShaped(canvas, run.Text, x, baselineY, SKTextAlign.Left, 0f, style, textSizePx);
                x += MeasureShapedWidth(run.Text, style, textSizePx);
            }
            else
            {
                using var paint = CreatePaint(run.Text, style, textSizePx);
                canvas.DrawText(run.Text, x, baselineY, paint);
                x += paint.MeasureText(run.Text);
            }
        }
    }

    private static float MeasureRun(TextRun run, SKFontStyle style, float textSizePx)
    {
        if (string.IsNullOrEmpty(run.Text))
            return 0;

        using var paint = CreatePaint(run.Text, style, textSizePx);
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
}
