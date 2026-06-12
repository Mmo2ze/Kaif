using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StoreAPI.Data;
using StoreShared.Backup;

namespace StoreAPI.Services;

public interface IDatabaseRestoreService
{
    Task<BackupRunResponse> RestoreFromUploadAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default);
}

/// <summary>Replaces the live SQLite database from a Discord backup zip or raw .db file.</summary>
public sealed class DatabaseRestoreService : IDatabaseRestoreService
{
    private const long MaxUploadBytes = 25L * 1024 * 1024;
    private static readonly SemaphoreSlim RestoreLock = new(1, 1);

    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SkuBarcodeImageService _barcodeCache;
    private readonly StoreRuntimeSettings _runtimeSettings;
    private readonly ILogger<DatabaseRestoreService> _logger;

    public DatabaseRestoreService(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        IServiceScopeFactory scopeFactory,
        SkuBarcodeImageService barcodeCache,
        StoreRuntimeSettings runtimeSettings,
        ILogger<DatabaseRestoreService> logger)
    {
        _configuration = configuration;
        _environment = environment;
        _scopeFactory = scopeFactory;
        _barcodeCache = barcodeCache;
        _runtimeSettings = runtimeSettings;
        _logger = logger;
    }

    public async Task<BackupRunResponse> RestoreFromUploadAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        if (fileStream.CanSeek && fileStream.Length > MaxUploadBytes)
            return new BackupRunResponse(false, "Backup file is too large (max 25 MB).");

        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName))
            return new BackupRunResponse(false, "Invalid file name.");

        var ext = Path.GetExtension(safeName);
        if (!ext.Equals(".zip", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".db", StringComparison.OrdinalIgnoreCase))
            return new BackupRunResponse(false, "Upload a .zip backup from Discord or a .db SQLite file.");

        var workDir = Path.Combine(Path.GetTempPath(), $"store-restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);

        try
        {
            var uploadedPath = Path.Combine(workDir, safeName);
            await using (var outStream = File.Create(uploadedPath))
                await fileStream.CopyToAsync(outStream, cancellationToken);

            if (new FileInfo(uploadedPath).Length > MaxUploadBytes)
                return new BackupRunResponse(false, "Backup file is too large (max 25 MB).");

            var sourceDbPath = ext.Equals(".zip", StringComparison.OrdinalIgnoreCase)
                ? ExtractDatabaseFromZip(uploadedPath, workDir)
                : uploadedPath;

            if (!await IsSqliteDatabaseAsync(sourceDbPath, cancellationToken))
                return new BackupRunResponse(false, "The file is not a valid SQLite database.");

            await RestoreLock.WaitAsync(cancellationToken);
            try
            {
                var liveDbPath = SqlitePathHelper.ResolveDatabaseFilePath(_configuration, _environment);
                await CloseLiveConnectionsAsync(cancellationToken);
                await CreatePreRestoreSafetyBackupAsync(liveDbPath, cancellationToken);
                await CopyDatabaseOverLiveAsync(sourceDbPath, liveDbPath, cancellationToken);
                await FinalizeAfterRestoreAsync(cancellationToken);
            }
            finally
            {
                RestoreLock.Release();
            }

            const string ok = "Database restored from backup. Refresh the app to load the restored data.";
            _logger.LogWarning("Database restored from uploaded backup: {File}", safeName);
            await AppendRestoreLogAsync($"RESTORE_OK {DateTime.UtcNow:O} {safeName}", cancellationToken);
            return new BackupRunResponse(true, ok);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database restore failed for {File}", safeName);
            await AppendRestoreLogAsync($"RESTORE_FAIL {DateTime.UtcNow:O} {safeName} {ex.Message}", cancellationToken);
            return new BackupRunResponse(false, ex.Message);
        }
        finally
        {
            try
            {
                if (Directory.Exists(workDir))
                    Directory.Delete(workDir, recursive: true);
            }
            catch
            {
                /* best effort */
            }
        }
    }

    private async Task CloseLiveConnectionsAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
        await db.Database.CloseConnectionAsync();
        SqliteConnection.ClearAllPools();
        await Task.Delay(100, ct);
    }

    private static string ExtractDatabaseFromZip(string zipPath, string workDir)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var dbEntry = archive.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name) &&
                        e.Name.EndsWith(".db", StringComparison.OrdinalIgnoreCase) &&
                        !e.FullName.Contains("..", StringComparison.Ordinal))
            .OrderByDescending(e => e.Name.StartsWith("store-backup-", StringComparison.OrdinalIgnoreCase))
            .ThenBy(e => e.FullName, StringComparer.Ordinal)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No .db file found inside the zip.");

        var extractedPath = Path.Combine(workDir, Path.GetFileName(dbEntry.Name)!);
        dbEntry.ExtractToFile(extractedPath, overwrite: true);
        return extractedPath;
    }

    private static async Task<bool> IsSqliteDatabaseAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
            return false;

        await using var stream = File.OpenRead(path);
        var header = new byte[16];
        var read = await stream.ReadAsync(header.AsMemory(0, 16), ct);
        if (read < 15)
            return false;

        return Encoding.ASCII.GetString(header, 0, 15) == "SQLite format 3";
    }

    private async Task CreatePreRestoreSafetyBackupAsync(string liveDbPath, CancellationToken ct)
    {
        if (!File.Exists(liveDbPath))
            return;

        var backupDir = Path.Combine(_environment.ContentRootPath, "backups");
        Directory.CreateDirectory(backupDir);
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture);
        var tempDb = Path.Combine(Path.GetTempPath(), $"store-pre-restore-{timestamp}_{Guid.NewGuid():N}.db");
        var zipPath = Path.Combine(backupDir, $"pre-restore-{timestamp}.zip");

        try
        {
            await CreateDatabaseSnapshotAsync(liveDbPath, tempDb, ct);
            CreateZipFromFile(tempDb, zipPath, $"pre-restore-{timestamp}.db");
            await AppendRestoreLogAsync($"PRE_RESTORE {DateTime.UtcNow:O} {Path.GetFileName(zipPath)}", ct);
        }
        finally
        {
            TryDelete(tempDb);
            TryDelete(tempDb + "-wal");
            TryDelete(tempDb + "-shm");
        }
    }

    private static async Task CopyDatabaseOverLiveAsync(string sourceDbPath, string liveDbPath, CancellationToken ct)
    {
        SqliteConnection.ClearAllPools();

        var liveDir = Path.GetDirectoryName(liveDbPath);
        if (!string.IsNullOrEmpty(liveDir))
            Directory.CreateDirectory(liveDir);

        TryDelete(liveDbPath);
        TryDelete(liveDbPath + "-wal");
        TryDelete(liveDbPath + "-shm");

        var sourceBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = sourceDbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        };
        var destBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = liveDbPath,
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
        TryDelete(liveDbPath + "-wal");
        TryDelete(liveDbPath + "-shm");
        await Task.Delay(100, ct);

        if (!File.Exists(liveDbPath))
            throw new InvalidOperationException("Restore did not create the database file.");
    }

    private async Task FinalizeAfterRestoreAsync(CancellationToken ct)
    {
        SqliteConnection.ClearAllPools();

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
        await DatabaseSchemaInitializer.ApplyAsync(db, ct);
        await DatabaseSeeder.SeedAsync(db);
        _barcodeCache.ClearCache();
        await _runtimeSettings.RefreshAsync();
    }

    private static async Task CreateDatabaseSnapshotAsync(string sourceDbPath, string tempDbPath, CancellationToken ct)
    {
        TryDelete(tempDbPath);
        TryDelete(tempDbPath + "-wal");
        TryDelete(tempDbPath + "-shm");

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
    }

    private static void CreateZipFromFile(string sourceFile, string zipPath, string entryName)
    {
        TryDelete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        zip.CreateEntryFromFile(sourceFile, entryName, CompressionLevel.SmallestSize);
    }

    private async Task AppendRestoreLogAsync(string line, CancellationToken ct)
    {
        try
        {
            var logsDir = Path.Combine(_environment.ContentRootPath, "logs");
            Directory.CreateDirectory(logsDir);
            await File.AppendAllTextAsync(Path.Combine(logsDir, "backup.log"), line + Environment.NewLine, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write backup.log");
        }
    }

    private static void TryDelete(string path)
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
}
