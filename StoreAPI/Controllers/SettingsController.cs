using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreAPI.Data;
using StoreAPI.Services;
using StoreShared;
using StoreShared.Pos;

namespace StoreAPI.Controllers;

[ApiController]
[Route("api/settings")]
public sealed class SettingsController : ControllerBase
{
    private readonly StoreDbContext _db;
    private readonly StoreRuntimeSettings _runtime;

    public SettingsController(StoreDbContext db, StoreRuntimeSettings runtime)
    {
        _db = db;
        _runtime = runtime;
    }

    [HttpGet]
    [AllowAnonymous]
    public ActionResult<PosSettingsDto> Get() => Ok(_runtime.ToDto());

    [HttpPut]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Put([FromBody] PosSettingsDto body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.StoreName))
            return BadRequest("Store name is required.");
        if (string.IsNullOrWhiteSpace(body.CurrencyLabel))
            return BadRequest("Currency label is required.");
        if (body.LowStockThreshold < 0)
            return BadRequest("Low stock threshold cannot be negative.");

        var row = await _db.StoreSettings.FirstOrDefaultAsync(s => s.Id == 1, ct);
        if (row is null)
        {
            row = new StoreSettings { Id = 1 };
            _db.StoreSettings.Add(row);
        }

        row.StoreName = body.StoreName.Trim();
        row.CurrencyLabel = body.CurrencyLabel.Trim();
        row.LowStockThreshold = body.LowStockThreshold;
        row.AllowSellerDiscount = body.AllowSellerDiscount;
        row.ReceiptAddress = body.ReceiptAddress?.Trim() ?? "";
        row.ReceiptLandline = body.ReceiptLandline?.Trim() ?? "";
        row.ReceiptPhone = body.ReceiptPhone?.Trim() ?? "";

        await _db.SaveChangesAsync(ct);
        await _runtime.RefreshAsync(ct);
        return NoContent();
    }
}
