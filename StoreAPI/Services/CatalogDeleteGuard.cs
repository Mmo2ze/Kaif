using Microsoft.EntityFrameworkCore;
using StoreAPI.Data;

namespace StoreAPI.Services;

/// <summary>Shared checks before deleting SKUs or product models that own SKUs.</summary>
public static class CatalogDeleteGuard
{
    public static async Task<string?> GetSkuBlockReasonAsync(StoreDbContext db, IReadOnlyList<int> skuIds, CancellationToken ct)
    {
        if (skuIds.Count == 0)
            return null;

        if (await db.SaleItems.AsNoTracking().AnyAsync(si => skuIds.Contains(si.SKUId), ct))
            return "Cannot delete because this product has sale history.";

        if (await db.StockAdjustments.AsNoTracking().AnyAsync(a => skuIds.Contains(a.SkuId), ct))
            return "Cannot delete because this product has stock adjustment history.";

        return null;
    }
}
