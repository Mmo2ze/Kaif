using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreAPI.Data;
using StoreAPI.Services;
using StoreShared;
using StoreShared.Catalog;

namespace StoreAPI.Controllers;

[ApiController]
[Route("api/products")]
[RequestSizeLimit(10_485_760)]
[RequestFormLimits(MultipartBodyLengthLimit = 10_485_760)]
public sealed class ProductsController : ControllerBase
{
    private static readonly JsonSerializerOptions ExportJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly StoreDbContext _db;
    private readonly ICatalogImportService _catalog;

    public ProductsController(StoreDbContext db, ICatalogImportService catalog)
    {
        _db = db;
        _catalog = catalog;
    }

    /// <summary>List product models (one row per model; SKUs are loaded per model via the skus sub-route).</summary>
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<ProductModelSummaryDto>>> List(CancellationToken ct)
    {
        var rows = await _db.ProductModels.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new ProductModelSummaryDto(
                p.Id,
                p.Name,
                p.Description,
                p.Skus.Count,
                p.BuyPrice,
                p.UnitPrice,
                p.SalePrice))
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<ProductModelSummaryDto>> Create([FromBody] CreateProductModelRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Name))
            return BadRequest("Name is required.");

        var buyPrice = body.BuyPrice < 0 ? 0 : body.BuyPrice;
        var unitPrice = body.UnitPrice < 0 ? 0 : body.UnitPrice;
        var salePrice = NormalizeSalePrice(body.SalePrice);
        if (salePrice is { } sp && sp >= unitPrice)
            return BadRequest("Sale price must be less than the unit price.");

        var entity = new ProductModel
        {
            Name = body.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim(),
            BuyPrice = buyPrice,
            UnitPrice = unitPrice,
            SalePrice = salePrice,
        };
        _db.ProductModels.Add(entity);
        await _db.SaveChangesAsync(ct);
        return Ok(ToSummary(entity, 0));
    }

    [HttpGet("{id:int}/skus")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<ProductSkuListRowDto>>> ListSkus(int id, CancellationToken ct)
    {
        var exists = await _db.ProductModels.AsNoTracking().AnyAsync(p => p.Id == id, ct);
        if (!exists)
            return NotFound();

        var skus = await _db.Skus.AsNoTracking()
            .Where(s => s.ProductModelId == id)
            .OrderBy(s => s.Size)
            .Select(s => new ProductSkuListRowDto(s.Id, s.Size, s.Barcode, s.Stock))
            .ToListAsync(ct);

        return Ok(skus);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<ProductModelSummaryDto>> Update(int id, [FromBody] UpdateProductModelRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Name))
            return BadRequest("Name is required.");

        var entity = await _db.ProductModels.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null)
            return NotFound();

        entity.Name = body.Name.Trim();
        entity.Description = string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim();
        await _db.SaveChangesAsync(ct);

        var skuCount = await _db.Skus.CountAsync(s => s.ProductModelId == id, ct);
        return Ok(ToSummary(entity, skuCount));
    }

    [HttpPut("{id:int}/price")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> UpdatePrice(int id, [FromBody] UpdateProductPriceRequest body, CancellationToken ct)
    {
        if (body.BuyPrice < 0)
            return BadRequest("Buy price cannot be negative.");
        if (body.UnitPrice < 0)
            return BadRequest("Unit price cannot be negative.");

        var salePrice = NormalizeSalePrice(body.SalePrice);
        if (salePrice is { } sp && sp >= body.UnitPrice)
            return BadRequest("Sale price must be less than the unit price.");

        var entity = await _db.ProductModels.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null)
            return NotFound();

        entity.BuyPrice = body.BuyPrice;
        entity.UnitPrice = body.UnitPrice;
        entity.SalePrice = salePrice;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Download all products, prices, and stock as JSON (for import on another machine).</summary>
    [HttpGet("export")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var file = await _catalog.ExportAsync(ct);
        var json = JsonSerializer.Serialize(file, ExportJsonOptions);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var fname = $"catalog-export-{DateTime.UtcNow:yyyyMMdd}.json";
        return File(bytes, "application/json", fname);
    }

    /// <summary>Merge product names/sizes from JSON. New sizes get stock 0; existing sizes keep stock. Does not touch sales or settings.</summary>
    [HttpPost("import")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<CatalogImportResultDto>> Import(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new CatalogImportResultDto(false, "Choose a catalog JSON file to upload.", 0, 0, 0, 0));

        if (!file.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new CatalogImportResultDto(false, "Upload a .json catalog export file.", 0, 0, 0, 0));

        await using var stream = file.OpenReadStream();
        var result = await _catalog.ImportAsync(stream, ct);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var entity = await _db.ProductModels.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null)
            return NotFound();

        var skuIds = await _db.Skus.Where(s => s.ProductModelId == id).Select(s => s.Id).ToListAsync(ct);
        if (skuIds.Count > 0)
        {
            var blockReason = await CatalogDeleteGuard.GetSkuBlockReasonAsync(_db, skuIds, ct);
            if (blockReason is not null)
                return Conflict(blockReason);
        }

        _db.ProductModels.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static ProductModelSummaryDto ToSummary(ProductModel entity, int skuCount) =>
        new(entity.Id, entity.Name, entity.Description, skuCount, entity.BuyPrice, entity.UnitPrice, entity.SalePrice);

    /// <summary>Zero or negative means "no sale".</summary>
    private static decimal? NormalizeSalePrice(decimal? salePrice) =>
        salePrice is { } s && s > 0 ? s : null;
}
