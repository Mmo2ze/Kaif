using System.Text.Json.Serialization;

namespace StoreShared.Backup;

public sealed record BackupSettingsAdminDto(
    [property: JsonPropertyName("discordWebhookUrl")] string DiscordWebhookUrl,
    [property: JsonPropertyName("backupIntervalHours")] int BackupIntervalHours,
    [property: JsonPropertyName("lastBackupUtc")] DateTime? LastBackupUtc);

public sealed record BackupSettingsUpdateDto(
    [property: JsonPropertyName("discordWebhookUrl")] string DiscordWebhookUrl,
    [property: JsonPropertyName("backupIntervalHours")] int BackupIntervalHours);

public sealed record BackupRunResponse(bool Success, string Message);
