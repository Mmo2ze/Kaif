using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreAPI.Data;
using StoreShared;
using StoreShared.Backup;

namespace StoreAPI.Controllers;

[ApiController]
[Route("api/settings/backup")]
[Authorize(Roles = nameof(UserRole.Admin))]
public sealed class BackupSettingsController : ControllerBase
{
    private readonly StoreDbContext _db;

    public BackupSettingsController(StoreDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<BackupSettingsAdminDto>> Get(CancellationToken ct)
    {
        var row = await _db.StoreSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1, ct);
        if (row is null)
            return Ok(new BackupSettingsAdminDto("", 24, null));

        var interval = row.BackupIntervalHours is 12 or 24 or 48 ? row.BackupIntervalHours : 24;
        return Ok(new BackupSettingsAdminDto(row.DiscordBackupWebhookUrl ?? "", interval, row.LastBackupUtc));
    }

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] BackupSettingsUpdateDto body, CancellationToken ct)
    {
        if (body.BackupIntervalHours is not (12 or 24 or 48))
            return BadRequest("Backup interval must be 12, 24, or 48 hours.");

        var row = await _db.StoreSettings.FirstOrDefaultAsync(s => s.Id == 1, ct);
        if (row is null)
        {
            row = new StoreSettings { Id = 1 };
            _db.StoreSettings.Add(row);
        }

        row.DiscordBackupWebhookUrl = body.DiscordWebhookUrl?.Trim() ?? "";
        row.BackupIntervalHours = body.BackupIntervalHours;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
