namespace StoreShared;

/// <summary>File-backed configuration for automated SQLite backups (see appsettings.json).</summary>
public sealed class BackupSettings
{
    public string DiscordWebhookUrl { get; set; } = "";

    public int IntervalHours { get; set; } = 24;

    /// <summary>Relative to the API content root unless rooted (same as connection string filename).</summary>
    public string DatabasePath { get; set; } = "store.db";

    public string BackupTempFolder { get; set; } = "backups";
}
