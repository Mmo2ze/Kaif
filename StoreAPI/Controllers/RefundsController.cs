using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoreAPI.Services;
using StoreShared;
using StoreShared.Sales;

namespace StoreAPI.Controllers;

[ApiController]
[Route("api/refunds")]
[Authorize(Roles = nameof(UserRole.Admin))]
public sealed class RefundsController : ControllerBase
{
    private readonly RefundService _refunds;

    public RefundsController(RefundService refunds) => _refunds = refunds;

    [HttpPost]
    public async Task<ActionResult<RefundResultDto>> Create([FromBody] RefundRequestDto body, CancellationToken ct)
    {
        var username = User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name ?? "admin";
        var result = await _refunds.ProcessRefundAsync(body, username, ct);
        if (!result.Success)
            return BadRequest(result);
        return Ok(result);
    }

    [HttpGet("{receiptNumber}")]
    public async Task<ActionResult<RefundHistoryDto>> GetByReceipt(string receiptNumber, CancellationToken ct)
    {
        var history = await _refunds.GetRefundHistoryAsync(receiptNumber, ct);
        if (history is null)
            return NotFound();
        return Ok(history);
    }
}
