using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoreAPI.Services;
using StoreShared;
using StoreShared.Print;

namespace StoreAPI.Controllers;

[ApiController]
[Route("api/print/labels")]
public sealed class PrintController : ControllerBase
{
    private readonly LabelPrintQueueService _queue;
    private readonly SkuLookupService _skuLookup;
    private readonly ILogger<PrintController> _log;

    public PrintController(LabelPrintQueueService queue, SkuLookupService skuLookup, ILogger<PrintController> log)
    {
        _queue = queue;
        _skuLookup = skuLookup;
        _log = log;
    }

    /// <summary>Queue barcode labels for the store computer (Store POS polls and prints).</summary>
    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Admin) + "," + nameof(UserRole.Seller))]
    public async Task<ActionResult<EnqueueLabelPrintResponse>> Enqueue(
        [FromBody] EnqueueLabelPrintRequest body,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Barcode))
            return BadRequest("Barcode is required.");

        var sku = await _skuLookup.FindByScanAsync(body.Barcode.Trim(), ct);
        if (sku is null)
        {
            _log.LogWarning("Print enqueue rejected — barcode not found: {Barcode}", body.Barcode);
            return NotFound("Product barcode not found.");
        }

        var user = User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "user";
        try
        {
            var result = _queue.Enqueue(sku.Barcode, body.Count, user);
            var msg =
                $"ENQUEUE job={result.JobId} barcode={sku.Barcode} count={body.Count} user={user} pending={_queue.PendingCount}";
            _log.LogInformation(msg);
            PrintApiLog.Write(msg);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            _log.LogWarning(ex, "Print queue full for barcode {Barcode}", sku.Barcode);
            return StatusCode(503, ex.Message);
        }
    }

    [HttpGet("next")]
    [Authorize]
    public ActionResult<LabelPrintJobDto> DequeueNext()
    {
        var job = _queue.PeekNext();
        if (job is null)
            return NoContent();

        _log.LogInformation("Print peek job {JobId} barcode {Barcode} count {Count}", job.Id, job.Barcode, job.Count);
        PrintApiLog.Write($"PEEK job={job.Id} barcode={job.Barcode} count={job.Count}");
        return Ok(job);
    }

    [HttpPost("{jobId:guid}/ack")]
    [Authorize]
    public IActionResult Acknowledge(Guid jobId)
    {
        if (!_queue.TryAcknowledge(jobId))
        {
            _log.LogWarning("Print ack failed — job {JobId} not at queue head", jobId);
            return NotFound();
        }

        _log.LogInformation("Print ack job {JobId} (pending={Pending})", jobId, _queue.PendingCount);
        PrintApiLog.Write($"ACK job={jobId} pending={_queue.PendingCount}");
        return NoContent();
    }

    [HttpGet("{jobId:guid}/pending")]
    [Authorize(Roles = nameof(UserRole.Admin) + "," + nameof(UserRole.Seller))]
    public IActionResult JobPending(Guid jobId)
    {
        var pending = _queue.IsQueued(jobId);
        _log.LogDebug("Print pending check job {JobId} => {Pending}", jobId, pending);
        return pending ? Ok() : NoContent();
    }

    [HttpGet("status")]
    [Authorize]
    public ActionResult<LabelPrintQueueStatusDto> Status() =>
        Ok(new LabelPrintQueueStatusDto(_queue.PendingCount));
}
