using Microsoft.EntityFrameworkCore;
using StoreAPI.Data;
using StoreShared;
using StoreShared.Barcode;

namespace StoreAPI.Services;

internal static class ProductSkuFactory
{
    public static async Task<int?> GetPrimarySkuIdAsync(StoreDbContext db, int productModelId, CancellationToken ct) =>
        await db.Skus.AsNoTracking()
            .Where(s => s.ProductModelId == productModelId)
            .OrderBy(s => s.Id)
            .Select(s => (int?)s.Id)
            .FirstOrDefaultAsync(ct);

    public static async Task<SKU> CreatePrimarySkuAsync(
        StoreDbContext db,
        int productModelId,
        int initialStock,
        CancellationToken ct)
    {
        var existing = await GetPrimarySkuIdAsync(db, productModelId, ct);
        if (existing is not null)
            throw new InvalidOperationException("Product already has a SKU.");

        var sku = new SKU
        {
            ProductModelId = productModelId,
            Size = ClothingSize.Custom,
            Stock = Math.Max(0, initialStock),
        };
        db.Skus.Add(sku);
        await db.SaveChangesAsync(ct);
        sku.Barcode = SkuBarcode.ForSkuId(sku.Id);
        await db.SaveChangesAsync(ct);
        return sku;
    }
}
