using StoreShared.Backup;

namespace StoreAPI.Services;

public interface IBackupRunner
{
    Task<BackupRunResponse> RunOnceAsync(CancellationToken cancellationToken = default);
}
