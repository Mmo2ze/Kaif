using System;
using System.Text;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace StoreShared.Text;

/// <summary>Resolves fonts that can shape and draw Arabic script for label/receipt rasters.</summary>
public static class LabelFontResolver
{
    private const int ArabicAlef = '\u0627';

    private static readonly string[] LatinFontFamilies = ["Tahoma", "Segoe UI", "Arial"];
    private static readonly string[] ArabicFontFamilies = ["Tahoma", "Segoe UI", "Geeza Pro", "Arial Unicode MS", "Noto Sans Arabic", "Arial"];

    public static bool ContainsArabic(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        foreach (var rune in text.EnumerateRunes())
        {
            if (IsArabicRune(rune))
                return true;
        }

        return false;
    }

    public static bool IsArabicRune(Rune rune)
    {
        var v = rune.Value;
        return v is >= '\u0600' and <= '\u06FF'
            or >= '\u0750' and <= '\u077F'
            or >= '\u08A0' and <= '\u08FF'
            or >= '\uFB50' and <= '\uFDFF'
            or >= '\uFE70' and <= '\uFEFF';
    }

    public static SKTypeface ResolveTypeface(string text, SKFontStyle style)
    {
        if (ContainsArabic(text))
        {
            foreach (var typeface in EnumerateArabicTypefaces(style))
                return typeface;
        }

        foreach (var family in LatinFontFamilies)
        {
            var typeface = SKTypeface.FromFamilyName(family, style);
            if (IsUsable(typeface))
                return typeface;
        }

        return SKTypeface.FromFamilyName("Arial", style)
               ?? SKTypeface.FromFamilyName("sans-serif", style)
               ?? SKTypeface.CreateDefault();
    }

    private static IEnumerable<SKTypeface> EnumerateArabicTypefaces(SKFontStyle style)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var matched = SKFontManager.Default.MatchCharacter(ArabicAlef);
        if (TryAddTypeface(seen, matched, requireArabic: true, out var fromMatch))
            yield return fromMatch;

        matched = SKFontManager.Default.MatchCharacter("Tahoma", style.Weight, style.Width, style.Slant, ["ar"], (char)ArabicAlef);
        if (TryAddTypeface(seen, matched, requireArabic: true, out var fromTahoma))
            yield return fromTahoma;

        foreach (var family in ArabicFontFamilies)
        {
            var typeface = SKTypeface.FromFamilyName(family, style);
            if (TryAddTypeface(seen, typeface, requireArabic: true, out var fromFamily))
                yield return fromFamily;
        }

        foreach (var path in EnumerateFontFilePaths())
        {
            if (!File.Exists(path))
                continue;

            var typeface = SKTypeface.FromFile(path);
            if (TryAddTypeface(seen, typeface, requireArabic: true, out var fromFile))
                yield return fromFile;
        }
    }

    private static IEnumerable<string> EnumerateFontFilePaths()
    {
        var userFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        foreach (var file in new[] { "tahoma.ttf", "tahomabd.ttf", "segoeui.ttf", "segoeuib.ttf", "arial.ttf", "arialuni.ttf" })
            yield return Path.Combine(userFonts, file);

        if (OperatingSystem.IsWindows())
        {
            var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var winFonts = Path.Combine(winDir, "Fonts");
            foreach (var file in new[] { "tahoma.ttf", "tahomabd.ttf", "segoeui.ttf", "segoeuib.ttf", "arial.ttf", "arialuni.ttf" })
                yield return Path.Combine(winFonts, file);
        }

        if (OperatingSystem.IsMacOS())
        {
            foreach (var path in new[]
                     {
                         "/System/Library/Fonts/GeezaPro.ttc",
                         "/System/Library/Fonts/Supplemental/Arial Unicode.ttf",
                         "/System/Library/Fonts/Supplemental/Arial.ttf",
                         "/Library/Fonts/Arial Unicode.ttf",
                     })
                yield return path;
        }
    }

    private static bool TryAddTypeface(
        HashSet<string> seen,
        SKTypeface? typeface,
        bool requireArabic,
        out SKTypeface resolved)
    {
        resolved = null!;
        if (!IsUsable(typeface))
            return false;

        var key = typeface!.FamilyName;
        if (!seen.Add(key))
            return false;

        if (requireArabic && !CanRenderArabic(typeface))
            return false;

        resolved = typeface;
        return true;
    }

    private static bool IsUsable(SKTypeface? typeface) =>
        typeface is not null && !string.IsNullOrWhiteSpace(typeface.FamilyName);

    private static bool CanRenderArabic(SKTypeface typeface)
    {
        try
        {
            using var paint = new SKPaint
            {
                TextSize = 24,
                Typeface = typeface,
                IsAntialias = true,
            };
            using var shaper = new SKShaper(typeface);
            using var buffer = new HarfBuzzSharp.Buffer();
            buffer.AddUtf32("ما");
            buffer.Direction = HarfBuzzSharp.Direction.RightToLeft;
            buffer.Script = HarfBuzzSharp.Script.Arabic;
            buffer.Language = new HarfBuzzSharp.Language("ar");
            var shaped = shaper.Shape(buffer, paint);
            return shaped.Width > 2f;
        }
        catch
        {
            return false;
        }
    }
}
