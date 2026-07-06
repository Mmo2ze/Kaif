using StoreShared.Backup;

namespace StoreAPI.Services;

public interface IBackupRunner
{
    Task<BackupRunResponse> RunOnceAsync(CancellationToken cancellationToken = default);

    Task<BackupArchiveResponse> CreateArchiveAsync(CancellationToken cancellationToken = default);
}
