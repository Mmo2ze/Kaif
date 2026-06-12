using System.Globalization;

namespace StoreShared;

public static class PriceFormat
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    public static string Format(decimal amount, string currencyLabel)
    {
        var label = string.IsNullOrWhiteSpace(currencyLabel) ? "" : currencyLabel.Trim();
        return string.IsNullOrEmpty(label)
            ? amount.ToString("N2", EnUs)
            : $"{amount.ToString("N2", EnUs)} {label}";
    }
}
