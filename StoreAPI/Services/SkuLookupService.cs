using Microsoft.EntityFrameworkCore;
using StoreAPI.Data;
using StoreShared;
using StoreShared.Barcode;

namespace StoreAPI.Services;

public sealed class SkuLookupService
{
    private readonly StoreDbContext _db;

    public SkuLookupService(StoreDbContext db) => _db = db;

    public async Task<SKU?> FindByScanAsync(string? scanned, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(scanned))
            return null;

        var canonical = SkuBarcode.NormalizeScanned(scanned);
        if (canonical is null)
            return null;

        var sku = await _db.Skus.AsNoTracking()
            .Include(s => s.ProductModel)
            .FirstOrDefaultAsync(s => s.Barcode == canonical, ct);

        if (sku is not null)
            return sku;

        // Printed check digit may differ from DB; article 2###### still identifies the SKU.
        if (SkuBarcode.TryParseSkuId(canonical, out var skuId))
        {
            sku = await _db.Skus.AsNoTracking()
                .Include(s => s.ProductModel)
                .FirstOrDefaultAsync(s => s.Id == skuId, ct);

            if (sku is not null)
                return sku;
        }

        var article7 = canonical[..7];
        return await _db.Skus.AsNoTracking()
            .Include(s => s.ProductModel)
            .FirstOrDefaultAsync(s => s.Barcode.Length == 8 && s.Barcode.StartsWith(article7), ct);
    }
}
