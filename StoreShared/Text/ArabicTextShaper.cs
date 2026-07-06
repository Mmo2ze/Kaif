using HarfBuzzSharp;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace StoreShared.Text;

/// <summary>Shapes and draws Arabic in logical word order (e.g. بوكس فت reads بوكس then فت, RTL).</summary>
public static class ArabicTextShaper
{
    private const float WordSpaceRatio = 0.25f;

    public static float MeasureWidth(string text, SKPaint paint)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var words = SplitWords(text);
        if (words.Count <= 1)
            return MeasureSegment(text, paint);

        var space = WordSpacing(paint);
        return words.Sum(w => MeasureSegment(w, paint)) + space * (words.Count - 1);
    }

    public static void Draw(
        SKCanvas canvas,
        string text,
        float baselineY,
        SKPaint paint,
        HorizontalAlign align,
        float lineWidth,
        float x = 0f)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var totalWidth = MeasureWidth(text, paint);
        var rightEdge = align switch
        {
            HorizontalAlign.Center => (lineWidth + totalWidth) / 2f,
            HorizontalAlign.Right => lineWidth - x,
            _ => x + totalWidth,
        };
        DrawWordsFromRight(canvas, text, rightEdge, baselineY, paint);
    }

    public static void DrawAt(
        SKCanvas canvas,
        string text,
        float x,
        float baselineY,
        SKPaint paint)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var rightEdge = x + MeasureWidth(text, paint);
        DrawWordsFromRight(canvas, text, rightEdge, baselineY, paint);
    }

    /// <summary>Layout words in logical order from the right edge (بوكس on the right, فت to its left).</summary>
    private static void DrawWordsFromRight(
        SKCanvas canvas,
        string text,
        float rightEdge,
        float baselineY,
        SKPaint paint)
    {
        var words = SplitWords(text);
        if (words.Count <= 1)
        {
            var result = ShapeSegment(text, paint);
            DrawResult(canvas, result, rightEdge - result.Width, baselineY, paint);
            return;
        }

        using var shaper = new SKShaper(paint.Typeface);
        var cursor = rightEdge;
        var space = WordSpacing(paint);

        for (var i = 0; i < words.Count; i++)
        {
            var result = ShapeSegment(words[i], paint, shaper);
            cursor -= result.Width;
            DrawResult(canvas, result, cursor, baselineY, paint);
            if (i < words.Count - 1)
                cursor -= space;
        }
    }

    private static float MeasureSegment(string text, SKPaint paint)
    {
        using var shaper = new SKShaper(paint.Typeface);
        return ShapeSegment(text, paint, shaper).Width;
    }

    private static SKShaper.Result ShapeSegment(string text, SKPaint paint, SKShaper? shaper = null)
    {
        shaper ??= new SKShaper(paint.Typeface);
        using var buffer = new HarfBuzzSharp.Buffer();
        buffer.AddUtf32(text);
        buffer.Direction = Direction.RightToLeft;
        buffer.Script = Script.Arabic;
        buffer.Language = new Language("ar");
        return shaper.Shape(buffer, paint);
    }

    private static void DrawResult(SKCanvas canvas, SKShaper.Result result, float x, float y, SKPaint paint)
    {
        if (result.Codepoints is null || result.Points is null || result.Codepoints.Length == 0)
            return;

        using var font = new SKFont(paint.Typeface, paint.TextSize);
        using var builder = new SKTextBlobBuilder();
        var run = builder.AllocatePositionedRun(font, result.Codepoints.Length);
        var glyphs = run.GetGlyphSpan();
        var positions = run.GetPositionSpan();
        for (var i = 0; i < result.Codepoints.Length; i++)
        {
            glyphs[i] = (ushort)result.Codepoints[i];
            positions[i] = new SKPoint(x + result.Points[i].X, y + result.Points[i].Y);
        }

        using var blob = builder.Build();
        canvas.DrawText(blob, 0, 0, paint);
    }

    private static List<string> SplitWords(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

    private static float WordSpacing(SKPaint paint) => paint.TextSize * WordSpaceRatio;

    public enum HorizontalAlign
    {
        Left,
        Center,
        Right,
    }
}
