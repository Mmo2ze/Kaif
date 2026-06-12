namespace StoreShared.Barcode;

/// <summary>
/// EAN-8 retail barcodes for in-store SKUs (prefix 2). Supports up to 999,999 SKU ids; store needs ~1,500.
/// Stored value is 8 digits including check digit — matches typical scanner output.
/// </summary>
public static class SkuBarcode
{
    public const int MaxSkuId = 999_999;

    /// <summary>7-digit test article for printer/scanner checks (not a real SKU).</summary>
    public const string TestArticle7 = "2999999";

    /// <summary>8-digit barcode assigned from database SKU id.</summary>
    public static string ForSkuId(int skuId)
    {
        if (skuId is < 1 or > MaxSkuId)
            throw new ArgumentOutOfRangeException(nameof(skuId), "SKU id is out of range for EAN-8.");

        return WithCheckDigit(ToArticle7(skuId));
    }

    /// <summary>7 data digits passed to the encoder (check digit computed by the library too).</summary>
    public static string ToArticle7(int skuId) => $"2{skuId:D6}";

    public static string WithCheckDigit(ReadOnlySpan<char> dataDigits)
    {
        if (dataDigits.Length is not 7 and not 12)
            throw new ArgumentException("EAN check digit requires 7 (EAN-8) or 12 (EAN-13) data digits.", nameof(dataDigits));

        var sum = 0;
        for (var i = 0; i < dataDigits.Length; i++)
        {
            var d = dataDigits[i] - '0';
            if (d is < 0 or > 9)
                throw new ArgumentException("Barcode digits must be numeric.", nameof(dataDigits));
            sum += (i % 2 == 0) ? d : d * 3;
        }

        var check = (10 - sum % 10) % 10;
        return $"{dataDigits}{(char)('0' + check)}";
    }

    /// <summary>First 7 digits for image encoding from an 8-digit stored barcode.</summary>
    public static string GetArticle7(string barcode)
    {
        var digits = OnlyDigits(barcode);
        return digits.Length switch
        {
            8 => digits[..7],
            7 => digits,
            _ => throw new ArgumentException("SKU barcode must be 7 or 8 digits.", nameof(barcode)),
        };
    }

    /// <summary>Normalize wedge/keyboard input to canonical 8-digit form (our check digit).</summary>
    public static string? NormalizeScanned(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var digits = OnlyDigits(raw);
        if (digits.Length == 0)
            return null;

        // Some scanners prefix EAN-8 with zeros to 12/13 digits.
        if (digits.Length is 12 or 13 && digits[0] == '0')
        {
            var idx = digits.IndexOf('2');
            if (idx >= 0 && idx + 7 <= digits.Length)
                return WithCheckDigit(digits.AsSpan(idx, 7));
        }

        if (digits[0] != '2')
            return null;

        return digits.Length switch
        {
            >= 8 => WithCheckDigit(digits.AsSpan(0, 7)),
            7 => WithCheckDigit(digits),
            _ => null,
        };
    }

    /// <summary>SKU id encoded in article digits 2###### (check digit ignored).</summary>
    public static bool TryParseSkuId(string? canonical8Digits, out int skuId)
    {
        skuId = 0;
        if (string.IsNullOrEmpty(canonical8Digits))
            return false;

        var digits = OnlyDigits(canonical8Digits);
        if (digits.Length < 7 || digits[0] != '2')
            return false;

        return int.TryParse(digits.AsSpan(1, 6), out skuId) && skuId is >= 1 and <= MaxSkuId;
    }

    public static bool IsEan8Form(string? barcode)
    {
        if (string.IsNullOrEmpty(barcode))
            return false;
        var digits = OnlyDigits(barcode);
        return digits.Length == 8 && digits[0] == '2' && digits == WithCheckDigit(digits.AsSpan(0, 7));
    }

    private static string OnlyDigits(string value) =>
        string.Concat(value.Where(char.IsDigit));
}
