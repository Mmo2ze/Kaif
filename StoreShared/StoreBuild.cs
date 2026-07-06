namespace StoreShared;

/// <summary>Build markers shared by Store POS and StoreAPI (health check / cache bust).</summary>
public static class StoreBuild
{
    public const int ApiVersion = 2;

    /// <summary>Bump when label/receipt text rendering changes (clears barcode PNG cache on API start).</summary>
    public const string LabelRenderVersion = "F20";
}
