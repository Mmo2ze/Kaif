using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreAPI.Data;
using StoreAPI.Services;
using StoreShared;
using StoreShared.Barcode;
using StoreShared.Stock;

namespace StoreAPI.Controllers;

[ApiController]
[Route("api/stock")]
public sealed class StockController : ControllerBase
{
    private readonly StoreDbContext _db;
    private readonly StockService _stock;
    private readonly SkuBarcodeImageService _barcodePng;

    public StockController(StoreDbContext db, StockService stock, SkuBarcodeImageService barcodePng)
    {
        _db = db;
        _stock = stock;
        _barcodePng = barcodePng;
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<StockRowDto>>> List(CancellationToken ct)
    {
        var rows = await _db.Skus.AsNoTracking()
            .Include(s => s.ProductModel)
            .OrderBy(s => s.ProductModel!.Name)
            .ThenBy(s => s.Size)
            .ToListAsync(ct);

        var dtos = rows.Select(s => new StockRowDto(
            s.Id,
            s.ProductModelId,
            s.ProductModel?.Name ?? "",
            s.Size,
            s.Barcode,
            _barcodePng.ToPngBase64(s.Barcode, BarcodeImageKind.Compact),
            s.Stock)).ToList();

        return Ok(dtos);
    }

    [HttpPut("{skuId:int}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> SetStock(int skuId, [FromBody] SetStockRequest body, CancellationToken ct)
    {
        var (ok, error) = await _stock.SetStockAsync(skuId, body.Quantity, ct);
        if (!ok)
            return BadRequest(error);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{skuId:int}/add")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Add(int skuId, [FromBody] AdjustStockRequest body, CancellationToken ct)
    {
        var (ok, error) = await _stock.TryAddAsync(skuId, body.Quantity, ct);
        if (!ok)
            return BadRequest(error);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{skuId:int}/subtract")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Subtract(int skuId, [FromBody] AdjustStockRequest body, CancellationToken ct)
    {
        var (ok, error) = await _stock.TrySubtractAsync(skuId, body.Quantity, ct);
        if (!ok)
            return BadRequest(error);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("adjustments")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<PagedStockAdjustmentsResult>> ListAdjustments(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _db.StockAdjustments.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(from) && !string.IsNullOrWhiteSpace(to)
            && DateOnly.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fromD)
            && DateOnly.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.None, out var toD))
        {
            var (fromUtc, toUtc) = SalesAnalyticsService.RangeUtc(fromD, toD);
            query = query.Where(a => a.Timestamp >= fromUtc && a.Timestamp <= toUtc);
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(a => a.Sku!).ThenInclude(s => s.ProductModel)
            .ToListAsync(ct);

        var items = rows.Select(a => new StockAdjustmentDto(
            a.Id,
            a.SkuId,
            a.Sku?.ProductModel?.Name ?? "",
            a.Sku?.Size.ToString() ?? "",
            a.Sku?.Barcode ?? "",
            a.QuantityDelta,
            a.Reason,
            a.Timestamp,
            a.PerformedBy)).ToList();

        return Ok(new PagedStockAdjustmentsResult(items, total, page, pageSize));
    }
}
