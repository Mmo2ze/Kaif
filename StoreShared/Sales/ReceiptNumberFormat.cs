namespace StoreShared.Sales;

public static class ReceiptNumberFormat
{
    public const string SalePrefix = "RCP-";
    public const string RefundPrefix = "RFD-";

    public static string ForSale(int saleId) => $"{SalePrefix}{saleId:D5}";

    public static string ForRefund(int saleId, int partialSequence = 0) =>
        partialSequence <= 0
            ? $"{RefundPrefix}{saleId:D5}"
            : $"{RefundPrefix}{saleId:D5}-{partialSequence}";

    public static bool TryParseSaleId(string? input, out int saleId)
    {
        saleId = 0;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var s = input.Trim();
        string? numeric = null;

        if (s.StartsWith(SalePrefix, StringComparison.OrdinalIgnoreCase))
            numeric = s[SalePrefix.Length..];
        else if (s.StartsWith(RefundPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var rest = s[RefundPrefix.Length..];
            var dash = rest.IndexOf('-', StringComparison.Ordinal);
            numeric = dash >= 0 ? rest[..dash] : rest;
        }
        else
            return false;

        return int.TryParse(numeric, out saleId) && saleId > 0;
    }
}
