using System.Globalization;

namespace StorePOS;

/// <summary>Consistent money display: 1,250.00 EGP (en-US number grouping + label).</summary>
public static class MoneyFormat
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
