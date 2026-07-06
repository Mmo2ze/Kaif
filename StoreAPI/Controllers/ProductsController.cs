using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreAPI.Data;
using StoreAPI.Services;
using StoreShared;
using StoreShared.Barcode;
using StoreShared.Catalog;
using StoreShared.Stock;

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
    private readonly StockService _stock;
    private readonly SkuBarcodeImageService _barcodePng;

    public ProductsController(
        StoreDbContext db,
        ICatalogImportService catalog,
        StockService stock,
        SkuBarcodeImageService barcodePng)
    {
        _db = db;
        _catalog = catalog;
        _stock = stock;
        _barcodePng = barcodePng;
    }

    /// <summary>List products with stock, barcode, and prices (one row per product).</summary>
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<ProductModelSummaryDto>>> List(CancellationToken ct)
    {
        var models = await _db.ProductModels.AsNoTracking()
            .Include(p => p.Skus)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        var rows = models.Select(p =>
        {
            var primary = p.Skus.OrderBy(s => s.Id).FirstOrDefault();
            var stock = p.Skus.Sum(s => s.Stock);
            var barcode = primary?.Barcode ?? "";
            var png = string.IsNullOrEmpty(barcode)
                ? ""
                : _barcodePng.ToPngBase64(barcode, BarcodeImageKind.Compact);
            return new ProductModelSummaryDto(
                p.Id,
                p.Name,
                p.Description,
                primary?.Id ?? 0,
                barcode,
                png,
                stock,
                p.BuyPrice,
                p.UnitPrice,
                p.SalePrice);
        }).ToList();

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

        var sku = await ProductSkuFactory.CreatePrimarySkuAsync(_db, entity.Id, body.InitialStock, ct);
        var png = _barcodePng.ToPngBase64(sku.Barcode, BarcodeImageKind.Compact);
        return Ok(new ProductModelSummaryDto(
            entity.Id,
            entity.Name,
            entity.Description,
            sku.Id,
            sku.Barcode,
            png,
            sku.Stock,
            entity.BuyPrice,
            entity.UnitPrice,
            entity.SalePrice));
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
            .OrderBy(s => s.Id)
            .Select(s => new ProductSkuListRowDto(s.Id, s.Barcode, s.Stock))
            .ToListAsync(ct);

        return Ok(skus);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<ProductModelSummaryDto>> Update(int id, [FromBody] UpdateProductModelRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Name))
            return BadRequest("Name is required.");

        var entity = await _db.ProductModels.Include(p => p.Skus).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null)
            return NotFound();

        entity.Name = body.Name.Trim();
        entity.Description = string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim();
        await _db.SaveChangesAsync(ct);

        return Ok(ToSummary(entity));
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

    [HttpPut("{id:int}/stock")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> SetStock(int id, [FromBody] SetStockRequest body, CancellationToken ct)
    {
        var skuId = await ProductSkuFactory.GetPrimarySkuIdAsync(_db, id, ct);
        if (skuId is null)
            return NotFound("Product has no barcode yet.");

        var (ok, error) = await _stock.SetStockAsync(skuId.Value, body.Quantity, ct);
        if (!ok)
            return BadRequest(error);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:int}/stock/add")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> AddStock(int id, [FromBody] AdjustStockRequest body, CancellationToken ct)
    {
        var skuId = await ProductSkuFactory.GetPrimarySkuIdAsync(_db, id, ct);
        if (skuId is null)
            return NotFound("Product has no barcode yet.");

        var (ok, error) = await _stock.TryAddAsync(skuId.Value, body.Quantity, ct);
        if (!ok)
            return BadRequest(error);
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

    /// <summary>Merge products from JSON. New products get the listed stock; existing products keep stock unless listed in the file.</summary>
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

    private ProductModelSummaryDto ToSummary(ProductModel entity)
    {
        var primary = entity.Skus.OrderBy(s => s.Id).FirstOrDefault();
        var stock = entity.Skus.Sum(s => s.Stock);
        var barcode = primary?.Barcode ?? "";
        var png = string.IsNullOrEmpty(barcode)
            ? ""
            : _barcodePng.ToPngBase64(barcode, BarcodeImageKind.Compact);
        return new ProductModelSummaryDto(
            entity.Id,
            entity.Name,
            entity.Description,
            primary?.Id ?? 0,
            barcode,
            png,
            stock,
            entity.BuyPrice,
            entity.UnitPrice,
            entity.SalePrice);
    }

    /// <summary>Zero or negative means "no sale".</summary>
    private static decimal? NormalizeSalePrice(decimal? salePrice) =>
        salePrice is { } s && s > 0 ? s : null;
}
