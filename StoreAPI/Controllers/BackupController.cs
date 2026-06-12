using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreAPI.Data;
using StoreAPI.Services;
using StoreShared;
using StoreShared.Backup;

namespace StoreAPI.Controllers;

[ApiController]
[Route("api/backup")]
[Authorize(Roles = nameof(UserRole.Admin))]
[RequestSizeLimit(26_214_400)]
[RequestFormLimits(MultipartBodyLengthLimit = 26_214_400)]
public sealed class BackupController : ControllerBase
{
    private readonly StoreDbContext _db;
    private readonly IBackupRunner _backupRunner;
    private readonly IDatabaseRestoreService _restore;

    public BackupController(StoreDbContext db, IBackupRunner backupRunner, IDatabaseRestoreService restore)
    {
        _db = db;
        _backupRunner = backupRunner;
        _restore = restore;
    }

    [HttpPost("run-now")]
    public Task<BackupRunResponse> RunNow(CancellationToken ct) =>
        _backupRunner.RunOnceAsync(ct);

    /// <summary>Replace the live database with an uploaded Discord backup (.zip) or .db file.</summary>
    [HttpPost("restore")]
    public async Task<BackupRunResponse> Restore(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return new BackupRunResponse(false, "Choose a backup file to upload.");

        await using var stream = file.OpenReadStream();
        return await _restore.RestoreFromUploadAsync(stream, file.FileName, ct);
    }

    [HttpGet("last-run")]
    public async Task<ActionResult<object>> LastRun(CancellationToken ct)
    {
        var row = await _db.StoreSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1, ct);
        return Ok(new { lastBackupUtc = row?.LastBackupUtc });
    }
}
