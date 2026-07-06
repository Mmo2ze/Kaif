using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreAPI.Data;
using StoreAPI.Services;
using StoreShared;
using StoreShared.Barcode;
using StoreShared.Catalog;

namespace StoreAPI.Controllers;

[ApiController]
[Route("api/skus")]
public sealed class SkusController : ControllerBase
{
    private readonly StoreDbContext _db;
    private readonly SkuBarcodeImageService _barcodePng;
    private readonly SkuLookupService _skuLookup;

    public SkusController(StoreDbContext db, SkuBarcodeImageService barcodePng, SkuLookupService skuLookup)
    {
        _db = db;
        _barcodePng = barcodePng;
        _skuLookup = skuLookup;
    }

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public IActionResult Create([FromBody] CreateSkuRequest body) =>
        BadRequest("Sizes are no longer supported. Add a product instead — stock and barcode are created automatically.");

    [HttpGet("{barcode}")]
    [Authorize]
    public async Task<ActionResult<SkuDetailDto>> GetByBarcode(
        string barcode,
        [FromQuery] bool forLabelPrint = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            return BadRequest();

        var key = Uri.UnescapeDataString(barcode).Trim();
        var sku = await _skuLookup.FindByScanAsync(key, ct);
        if (sku is null)
            return NotFound();

        var (buyPrice, unitPrice, salePrice) = CatalogPricing.ForSku(sku);

        string png;
        if (forLabelPrint)
        {
            var settings = await _db.StoreSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1, ct);
            var currency = string.IsNullOrWhiteSpace(settings?.CurrencyLabel) ? "EGP" : settings.CurrencyLabel.Trim();
            var onSale = Pricing.IsOnSale(unitPrice, salePrice);
            var priceText = $"{Pricing.Effective(unitPrice, salePrice):N2} {currency}";
            var label = new SkuLabelContent(
                "Kaif",
                sku.ProductModel?.Name ?? "Product",
                priceText,
                sku.Barcode,
                onSale ? $"{unitPrice:N2}" : null);
            png = _barcodePng.ToFullLabelPngBase64(label);
        }
        else
        {
            png = _barcodePng.ToPngBase64(sku.Barcode, BarcodeImageKind.Standard);
        }

        return Ok(ToDetailDto(sku, png, buyPrice, unitPrice, salePrice));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var sku = await _db.Skus.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sku is null)
            return NotFound();

        var blockReason = await CatalogDeleteGuard.GetSkuBlockReasonAsync(_db, [sku.Id], ct);
        if (blockReason is not null)
            return Conflict(blockReason);

        _db.Skus.Remove(sku);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static SkuDetailDto ToDetailDto(SKU sku, string png) =>
        ToDetailDto(sku, png, CatalogPricing.ForSku(sku));

    private static SkuDetailDto ToDetailDto(SKU sku, string png, decimal buyPrice, decimal unitPrice, decimal? salePrice) =>
        new(
            sku.Id,
            sku.ProductModelId,
            sku.ProductModel?.Name,
            sku.Barcode,
            png,
            sku.Stock,
            buyPrice,
            unitPrice,
            salePrice);

    private static SkuDetailDto ToDetailDto(SKU sku, string png, (decimal BuyPrice, decimal UnitPrice, decimal? SalePrice) prices) =>
        ToDetailDto(sku, png, prices.BuyPrice, prices.UnitPrice, prices.SalePrice);
}
