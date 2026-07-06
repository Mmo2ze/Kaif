namespace StoreShared;

/// <summary>Singleton store row (Id = 1) for POS branding and behavior.</summary>
public class StoreSettings
{
    public int Id { get; set; } = 1;

    public string StoreName { get; set; } = "Kaif";

    public string CurrencyLabel { get; set; } = "EGP";

    /// <summary>Store address printed on receipts below the store name (optional).</summary>
    public string ReceiptAddress { get; set; } = "";

    /// <summary>Landline printed on thermal receipt footer (optional).</summary>
    public string ReceiptLandline { get; set; } = "";

    /// <summary>Mobile/phone printed on thermal receipt footer (optional).</summary>
    public string ReceiptPhone { get; set; } = "";

    public int LowStockThreshold { get; set; } = 5;

    public bool AllowSellerDiscount { get; set; }

    /// <summary>Discord webhook URL for DB zip uploads; empty = use appsettings only.</summary>
    public string DiscordBackupWebhookUrl { get; set; } = "";

    /// <summary>Hours between backups (12, 24, or 48). Stored in DB; 0 means fall back to appsettings default.</summary>
    public int BackupIntervalHours { get; set; } = 24;

    public DateTime? LastBackupUtc { get; set; }
}
