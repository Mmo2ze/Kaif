using System.Text;
using SkiaSharp;
using StoreShared.Text;

namespace StoreShared.Text;

/// <summary>Splits product/receipt strings into Arabic vs Latin runs for bidi layout.</summary>
public static class BidiTextRuns
{
    public readonly record struct Run(string Text, bool IsArabic);

    public static bool HasMixedScripts(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        var hasArabic = false;
        var hasNonArabic = false;
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
                continue;

            if (LabelFontResolver.IsArabicRune(rune))
                hasArabic = true;
            else
                hasNonArabic = true;

            if (hasArabic && hasNonArabic)
                return true;
        }

        return false;
    }

    /// <summary>Spaces between Arabic words stay in the Arabic run; Latin size suffixes become their own run.</summary>
    public static List<Run> Split(string text)
    {
        var runs = new List<Run>();
        if (string.IsNullOrEmpty(text))
            return runs;

        var runes = text.EnumerateRunes().ToArray();
        var i = 0;
        while (i < runes.Length)
        {
            if (LabelFontResolver.IsArabicRune(runes[i]) || IsArabicContinuedSpace(runes, i))
            {
                var sb = new StringBuilder();
                while (i < runes.Length)
                {
                    if (LabelFontResolver.IsArabicRune(runes[i]))
                    {
                        sb.Append(runes[i]);
                        i++;
                        continue;
                    }

                    if (Rune.IsWhiteSpace(runes[i]) && IsArabicContinuedSpace(runes, i))
                    {
                        sb.Append(runes[i]);
                        i++;
                        continue;
                    }

                    break;
                }

                if (sb.Length > 0)
                    runs.Add(new Run(sb.ToString(), true));
                continue;
            }

            var latin = new StringBuilder();
            while (i < runes.Length)
            {
                if (LabelFontResolver.IsArabicRune(runes[i]))
                    break;
                if (Rune.IsWhiteSpace(runes[i]) && IsArabicContinuedSpace(runes, i))
                    break;

                latin.Append(runes[i]);
                i++;
            }

            if (latin.Length > 0)
                runs.Add(new Run(latin.ToString(), false));
        }

        return runs;
    }

    public static float MeasureRun(Run run, SKPaint paint, SKFontStyle style, float textSize)
    {
        if (string.IsNullOrEmpty(run.Text))
            return 0;

        using var runPaint = CreatePaint(run.Text, style, textSize, paint.Typeface);
        if (run.IsArabic)
            return ArabicTextShaper.MeasureWidth(run.Text, runPaint);

        return runPaint.MeasureText(run.Text);
    }

    public static void DrawRunsCentered(
        SKCanvas canvas,
        IReadOnlyList<Run> runs,
        float baselineY,
        SKFontStyle style,
        float textSize,
        float lineWidth)
    {
        if (runs.Count == 0)
            return;

        using var samplePaint = CreatePaint("A", style, textSize, LabelFontResolver.ResolveTypeface("A", style));
        var totalWidth = runs.Sum(r => MeasureRun(r, samplePaint, style, textSize));

        // Logical order: Arabic name then Latin suffix (e.g. قميص بوكس فت - M).
        var x = (lineWidth - totalWidth) / 2f;
        DrawRunsAt(canvas, runs, x, baselineY, style, textSize, samplePaint.Typeface);
    }

    public static void DrawRunsLeft(
        SKCanvas canvas,
        IReadOnlyList<Run> runs,
        float x,
        float baselineY,
        SKFontStyle style,
        float textSize)
    {
        // Logical order: quantity prefix, Arabic name, Latin size (e.g. 1x قميص بوكس فت M).
        var typeface = LabelFontResolver.ResolveTypeface("A", style);
        DrawRunsAt(canvas, runs, x, baselineY, style, textSize, typeface);
    }

    private static void DrawRunsAt(
        SKCanvas canvas,
        IReadOnlyList<Run> runs,
        float x,
        float baselineY,
        SKFontStyle style,
        float textSize,
        SKTypeface typeface)
    {
        foreach (var run in runs)
        {
            if (string.IsNullOrEmpty(run.Text))
                continue;

            using var paint = CreatePaint(run.Text, style, textSize, typeface);
            if (run.IsArabic)
            {
                ArabicTextShaper.DrawAt(canvas, run.Text, x, baselineY, paint);
                x += ArabicTextShaper.MeasureWidth(run.Text, paint);
            }
            else
            {
                canvas.DrawText(run.Text, x, baselineY, paint);
                x += paint.MeasureText(run.Text);
            }
        }
    }

    public static float MeasureRunsWidth(IReadOnlyList<Run> runs, SKFontStyle style, float textSize)
    {
        using var samplePaint = CreatePaint("A", style, textSize, LabelFontResolver.ResolveTypeface("A", style));
        return runs.Sum(r => MeasureRun(r, samplePaint, style, textSize));
    }

    private static SKPaint CreatePaint(string text, SKFontStyle style, float textSize, SKTypeface? typeface = null)
    {
        return new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true,
            TextSize = textSize,
            Typeface = typeface ?? LabelFontResolver.ResolveTypeface(text, style),
        };
    }

    private static bool IsArabicContinuedSpace(Rune[] runes, int index)
    {
        if (index >= runes.Length || !Rune.IsWhiteSpace(runes[index]))
            return false;

        for (var next = index + 1; next < runes.Length; next++)
        {
            if (Rune.IsWhiteSpace(runes[next]))
                continue;

            return LabelFontResolver.IsArabicRune(runes[next]);
        }

        return false;
    }
}
