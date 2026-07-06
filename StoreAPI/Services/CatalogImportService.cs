using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StoreAPI.Data;
using StoreShared;
using StoreShared.Barcode;
using StoreShared.Catalog;

namespace StoreAPI.Services;

public interface ICatalogImportService
{
    Task<CatalogExportFileDto> ExportAsync(CancellationToken cancellationToken = default);

    Task<CatalogImportResultDto> ImportAsync(Stream jsonStream, CancellationToken cancellationToken = default);
}

public sealed class CatalogImportService : ICatalogImportService
{
    private const int MaxProducts = 10_000;
    private const long MaxImportBytes = 10 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly StoreDbContext _db;
    private readonly SkuBarcodeImageService _barcodeCache;

    public CatalogImportService(StoreDbContext db, SkuBarcodeImageService barcodeCache)
    {
        _db = db;
        _barcodeCache = barcodeCache;
    }

    public async Task<CatalogExportFileDto> ExportAsync(CancellationToken cancellationToken = default)
    {
        var models = await _db.ProductModels.AsNoTracking()
            .Include(p => p.Skus)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        var products = models.Select(p => new CatalogExportProductDto(
            p.Name,
            p.BuyPrice,
            p.UnitPrice,
            p.SalePrice,
            p.Skus.Sum(s => s.Stock)))
            .ToList();

        return new CatalogExportFileDto(2, DateTime.UtcNow, products);
    }

    public async Task<CatalogImportResultDto> ImportAsync(Stream jsonStream, CancellationToken cancellationToken = default)
    {
        if (jsonStream.CanSeek && jsonStream.Length > MaxImportBytes)
            return Fail("Catalog file is too large (max 10 MB).");

        CatalogExportFileDto? file;
        try
        {
            file = await JsonSerializer.DeserializeAsync<CatalogExportFileDto>(jsonStream, JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Fail($"Invalid catalog file: {ex.Message}");
        }

        if (file is null || file.Products.Count == 0)
            return Fail("Catalog file is empty.");

        if (file.Products.Count > MaxProducts)
            return Fail($"Too many products (max {MaxProducts}).");

        var existing = await _db.ProductModels
            .Include(p => p.Skus)
            .ToListAsync(cancellationToken);

        var byName = existing.ToDictionary(
            p => NormalizeName(p.Name),
            StringComparer.OrdinalIgnoreCase);

        var productsCreated = 0;
        var productsUpdated = 0;
        var skusCreated = 0;

        foreach (var exported in file.Products)
        {
            if (string.IsNullOrWhiteSpace(exported.Name))
                continue;

            var key = NormalizeName(exported.Name);
            var isLegacy = file.Version < 2;
            var stock = isLegacy ? 0 : Math.Max(0, exported.Stock);

            if (!byName.TryGetValue(key, out var model))
            {
                model = new ProductModel
                {
                    Name = exported.Name.Trim(),
                    BuyPrice = isLegacy ? 0 : Math.Max(0, exported.BuyPrice),
                    UnitPrice = isLegacy ? 0 : Math.Max(0, exported.UnitPrice),
                    SalePrice = isLegacy ? null : NormalizeSalePrice(exported.SalePrice, exported.UnitPrice),
                };
                _db.ProductModels.Add(model);
                await _db.SaveChangesAsync(cancellationToken);
                byName[key] = model;
                productsCreated++;

                var sku = await ProductSkuFactory.CreatePrimarySkuAsync(_db, model.Id, stock, cancellationToken);
                skusCreated++;
                _ = sku;
                continue;
            }

            if (!isLegacy)
            {
                model.BuyPrice = Math.Max(0, exported.BuyPrice);
                model.UnitPrice = Math.Max(0, exported.UnitPrice);
                model.SalePrice = NormalizeSalePrice(exported.SalePrice, exported.UnitPrice);
                productsUpdated++;
            }

            var primaryId = await ProductSkuFactory.GetPrimarySkuIdAsync(_db, model.Id, cancellationToken);
            if (primaryId is null)
            {
                await ProductSkuFactory.CreatePrimarySkuAsync(_db, model.Id, stock, cancellationToken);
                skusCreated++;
            }
            else if (!isLegacy && exported.Stock > 0)
            {
                var primary = model.Skus.OrderBy(s => s.Id).FirstOrDefault(s => s.Id == primaryId);
                if (primary is not null)
                    primary.Stock = Math.Max(0, exported.Stock);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        _barcodeCache.ClearCache();

        var message =
            $"Imported catalog: {productsCreated} product(s) added, {productsUpdated} updated, {skusCreated} barcode(s) created.";
        return new CatalogImportResultDto(true, message, productsCreated, productsUpdated, skusCreated, 0);
    }

    private static decimal? NormalizeSalePrice(decimal? salePrice, decimal unitPrice)
    {
        if (salePrice is not { } s || s <= 0 || s >= unitPrice)
            return null;
        return s;
    }

    private static string NormalizeName(string name) => name.Trim();

    private static CatalogImportResultDto Fail(string message) =>
        new(false, message, 0, 0, 0, 0);
}
