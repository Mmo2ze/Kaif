using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StoreAPI.Data;
using StoreShared;
using StoreShared.Backup;

namespace StoreAPI.Services;

/// <summary>Periodic SQLite backup to zip, upload to Discord webhook, log to file.</summary>
public sealed class DatabaseBackupService : BackgroundService, IBackupRunner
{
    public const string HttpClientName = "DiscordBackup";

    private const long DiscordMaxAttachmentBytes = 25L * 1024 * 1024;

    private readonly IOptions<BackupSettings> _options;
    private readonly ILogger<DatabaseBackupService> _logger;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public DatabaseBackupService(
        IOptions<BackupSettings> options,
        ILogger<DatabaseBackupService> logger,
        IHttpClientFactory httpFactory,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _options = options;
        _logger = logger;
        _httpFactory = httpFactory;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _environment = environment;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunScheduledBackupAsync(stoppingToken);
            try
            {
                var hours = await GetEffectiveIntervalHoursAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromHours(hours), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    public async Task<BackupRunResponse> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await RunBackupInternalAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup failed");
            await AppendBackupLogAsync($"FAIL {DateTime.UtcNow:O} {ex.Message}", cancellationToken);
            return new BackupRunResponse(false, ex.Message);
        }
    }

    private async Task RunScheduledBackupAsync(CancellationToken ct)
    {
        try
        {
            var result = await RunBackupInternalAsync(ct);
            if (!result.Success)
                _logger.LogWarning("Scheduled backup did not complete: {Message}", result.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled backup failed");
            await AppendBackupLogAsync($"FAIL {DateTime.UtcNow:O} {ex.Message}", ct);
        }
    }

    private async Task<BackupRunResponse> RunBackupInternalAsync(CancellationToken ct)
    {
        var webhook = await GetEffectiveWebhookAsync(ct);
        if (string.IsNullOrWhiteSpace(webhook) ||
            webhook.Contains("YOUR_ID", StringComparison.OrdinalIgnoreCase))
        {
            const string msg = "Discord webhook URL is not configured.";
            _logger.LogWarning("{Msg}", msg);
            await AppendBackupLogAsync($"SKIP {DateTime.UtcNow:O} {msg}", ct);
            return new BackupRunResponse(false, msg);
        }

        var s = _options.Value;
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm", System.Globalization.CultureInfo.InvariantCulture);
        var zipName = $"store-backup-{timestamp}.zip";
        var zipDir = Path.Combine(_environment.ContentRootPath, s.BackupTempFolder);
        var zipPath = Path.Combine(zipDir, zipName);
        var tempDb = Path.Combine(Path.GetTempPath(), $"store-backup-{timestamp}_{Guid.NewGuid():N}.db");

        Directory.CreateDirectory(zipDir);

        var sourceDbPath = SqlitePathHelper.ResolveDatabaseFilePath(_configuration, _environment);

        await CreateDatabaseSnapshotAsync(sourceDbPath, tempDb, ct);

        try
        {
            await CreateZipFromFileAsync(tempDb, zipPath, $"store-backup-{timestamp}.db", ct);
        }
        finally
        {
            TryDeleteFile(tempDb);
            TryDeleteFile(tempDb + "-wal");
            TryDeleteFile(tempDb + "-shm");
        }

        var zipInfo = new FileInfo(zipPath);
        if (zipInfo.Length > DiscordMaxAttachmentBytes)
        {
            try
            {
                File.Delete(zipPath);
            }
            catch
            {
                /* ignore */
            }

            var msg =
                $"Backup zip is {zipInfo.Length} bytes; Discord webhooks limit attachments to 25 MB.";
            _logger.LogWarning("{Msg}", msg);
            await AppendBackupLogAsync($"FAIL {DateTime.UtcNow:O} {msg}", ct);
            return new BackupRunResponse(false, msg);
        }

        try
        {
            await SendToDiscordAsync(webhook, zipPath, zipName, timestamp, ct);
        }
        finally
        {
            try
            {
                if (File.Exists(zipPath))
                    File.Delete(zipPath);
            }
            catch
            {
                /* ignore */
            }
        }

        await MarkLastBackupAsync(ct);

        _logger.LogInformation("Backup successful: {Name} at {Time}", zipName, timestamp);
        var okLine = $"OK {DateTime.UtcNow:O} {zipName}";
        await AppendBackupLogAsync(okLine, ct);

        return new BackupRunResponse(true, "Backup sent to Discord.");
    }

    /// <summary>
    /// Copies the live SQLite file via the backup API. Pooling is disabled so the temp file
    /// is not kept open by the connection pool (that caused "file is being used by another process" on Windows).
    /// </summary>
    private static async Task CreateDatabaseSnapshotAsync(string sourceDbPath, string tempDbPath, CancellationToken ct)
    {
        TryDeleteFile(tempDbPath);
        TryDeleteFile(tempDbPath + "-wal");
        TryDeleteFile(tempDbPath + "-shm");

        // BackupDatabase avoids VACUUM INTO path issues on Windows (backslashes → SQLite I/O error 10).
        var sourceBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = sourceDbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        };
        var destBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = tempDbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        };

        await using (var sourceConn = new SqliteConnection(sourceBuilder.ToString()))
        {
            await sourceConn.OpenAsync(ct);
            await using var destConn = new SqliteConnection(destBuilder.ToString());
            await destConn.OpenAsync(ct);
            sourceConn.BackupDatabase(destConn);
            await destConn.CloseAsync();
            await sourceConn.CloseAsync();
        }

        SqliteConnection.ClearAllPools();
        await Task.Delay(100, ct);

        if (!File.Exists(tempDbPath))
            throw new InvalidOperationException("Backup snapshot was not created.");
    }

    private static async Task CreateZipFromFileAsync(
        string sourceFile,
        string zipPath,
        string entryName,
        CancellationToken ct)
    {
        const int maxAttempts = 8;
        if (File.Exists(zipPath))
            TryDeleteFile(zipPath);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                var entry = zip.CreateEntry(entryName, CompressionLevel.SmallestSize);
                await using var entryStream = entry.Open();
                await using var fileStream = new FileStream(
                    sourceFile,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 81920,
                    useAsync: true);
                await fileStream.CopyToAsync(entryStream, ct);
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                SqliteConnection.ClearAllPools();
                await Task.Delay(50 * attempt, ct);
                if (File.Exists(zipPath))
                    TryDeleteFile(zipPath);
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            /* best effort */
        }
    }

    private async Task<string?> GetEffectiveWebhookAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
        var row = await db.StoreSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1, ct);
        var fromDb = row?.DiscordBackupWebhookUrl?.Trim();
        if (!string.IsNullOrEmpty(fromDb))
            return fromDb;

        var fromConfig = _options.Value.DiscordWebhookUrl?.Trim();
        if (!string.IsNullOrEmpty(fromConfig))
            return fromConfig;

        // Raw key in case options binding ever misses; env for headless deploy (STORE_BACKUP_DISCORD_WEBHOOK).
        var raw = _configuration["BackupSettings:DiscordWebhookUrl"]?.Trim()
                  ?? Environment.GetEnvironmentVariable("STORE_BACKUP_DISCORD_WEBHOOK")?.Trim();
        return string.IsNullOrEmpty(raw) ? null : raw;
    }

    private async Task<int> GetEffectiveIntervalHoursAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
        var row = await db.StoreSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1, ct);
        var fromDb = row?.BackupIntervalHours ?? 0;
        if (fromDb is 12 or 24 or 48)
            return fromDb;

        var fallback = _options.Value.IntervalHours;
        return fallback is 12 or 24 or 48 ? fallback : 24;
    }

    private async Task MarkLastBackupAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
        var row = await db.StoreSettings.FirstOrDefaultAsync(s => s.Id == 1, ct);
        if (row is null)
            return;
        row.LastBackupUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task SendToDiscordAsync(
        string webhookUrl,
        string zipPath,
        string zipName,
        string timestamp,
        CancellationToken ct)
    {
        var http = _httpFactory.CreateClient(HttpClientName);
        await using var fileStream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        using var content = new MultipartFormDataContent();
        var payload = new
        {
            content = "📦 **Database Backup**\n" +
                      $"🕐 `{timestamp}`\n" +
                      $"📁 `{zipName}`\n" +
                      "✅ Automatic backup — store system",
        };
        var json = JsonSerializer.Serialize(payload);
        content.Add(new StringContent(json, Encoding.UTF8, MediaTypeHeaderValue.Parse("application/json")), "payload_json");

        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(fileContent, "files[0]", zipName);

        using var response = await http.PostAsync(webhookUrl, content, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Discord returned {response.StatusCode}: {body}");
        }
    }

    private async Task AppendBackupLogAsync(string line, CancellationToken ct)
    {
        try
        {
            var logsDir = Path.Combine(_environment.ContentRootPath, "logs");
            Directory.CreateDirectory(logsDir);
            var path = Path.Combine(logsDir, "backup.log");
            await File.AppendAllTextAsync(path, line + Environment.NewLine, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write backup.log");
        }
    }
}
