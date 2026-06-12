namespace StoreShared.Barcode;

public enum BarcodeImageKind
{
    /// <summary>Catalog / detail view (~420×140).</summary>
    Standard,

    /// <summary>Stock table thumbnail (no text label).</summary>
    Compact,

    /// <summary>High-resolution label for thermal printers (e.g. Xprinter XP-410B).</summary>
    Label,
}
