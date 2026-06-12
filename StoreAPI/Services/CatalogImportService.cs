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
    private const int MaxSkusPerProduct = 64;
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
            p.Skus.OrderBy(s => s.Size).Select(s => s.Size).ToList()))
            .ToList();

        return new CatalogExportFileDto(1, DateTime.UtcNow, products);
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

        if (file.Version is not (0 or 1))
            return Fail($"Unsupported catalog version ({file.Version}).");

        if (file.Products.Count > MaxProducts)
            return Fail($"Too many products (max {MaxProducts}).");

        var existing = await _db.ProductModels
            .Include(p => p.Skus)
            .ToListAsync(cancellationToken);

        var byName = existing.ToDictionary(
            p => NormalizeName(p.Name),
            StringComparer.OrdinalIgnoreCase);

        var productsCreated = 0;
        var skusCreated = 0;
        var skusSkipped = 0;

        foreach (var exported in file.Products)
        {
            if (string.IsNullOrWhiteSpace(exported.Name))
                continue;

            var sizes = ResolveSizes(exported);
            if (sizes.Count == 0)
                continue;

            if (sizes.Count > MaxSkusPerProduct)
                return Fail($"Product \"{exported.Name.Trim()}\" has too many sizes.");

            var key = NormalizeName(exported.Name);
            if (!byName.TryGetValue(key, out var model))
            {
                model = new ProductModel { Name = exported.Name.Trim() };
                _db.ProductModels.Add(model);
                await _db.SaveChangesAsync(cancellationToken);
                byName[key] = model;
                productsCreated++;
            }

            var skuBySize = model.Skus.ToDictionary(s => s.Size);
            foreach (var size in sizes.Distinct())
            {
                if (skuBySize.ContainsKey(size))
                {
                    skusSkipped++;
                    continue;
                }

                var sku = new SKU
                {
                    ProductModelId = model.Id,
                    Size = size,
                    Stock = 0,
                };
                _db.Skus.Add(sku);
                await _db.SaveChangesAsync(cancellationToken);
                sku.Barcode = SkuBarcode.ForSkuId(sku.Id);
                skuBySize[size] = sku;
                skusCreated++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        _barcodeCache.ClearCache();

        var message =
            $"Imported catalog: {productsCreated} product(s) added, {skusCreated} new size(s) added (stock 0). " +
            $"{skusSkipped} size(s) already existed — stock unchanged.";
        return new CatalogImportResultDto(true, message, productsCreated, 0, skusCreated, skusSkipped);
    }

    private static IReadOnlyList<ClothingSize> ResolveSizes(CatalogExportProductDto exported)
    {
        if (exported.Sizes is { Count: > 0 })
            return exported.Sizes;

        if (exported.Skus is { Count: > 0 })
            return exported.Skus.Select(s => s.Size).ToList();

        return Array.Empty<ClothingSize>();
    }

    private static string NormalizeName(string name) => name.Trim();

    private static CatalogImportResultDto Fail(string message) =>
        new(false, message, 0, 0, 0, 0);
}
