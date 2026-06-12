using StoreAPI.Data;

namespace StoreAPI.Services;

/// <summary>Applies SQLite schema patches after startup or database restore.</summary>
public static class DatabaseSchemaInitializer
{
    public static async Task ApplyAsync(StoreDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken);
        await db.EnsureSkuUnitPriceColumnAsync(cancellationToken);
        await db.EnsureSkuSalePriceColumnAsync(cancellationToken);
        await db.EnsureSkuBuyPriceColumnAsync(cancellationToken);
        await db.EnsureProductModelPricingColumnsAsync(cancellationToken);
        await db.EnsureSaleItemUnitCostColumnAsync(cancellationToken);
        await db.EnsureSaleEventLineUnitCostColumnAsync(cancellationToken);
        await db.EnsurePhase9SchemaAsync(cancellationToken);
        await db.EnsureBackupSettingsColumnsAsync(cancellationToken);
        await db.EnsureSaleEventsSchemaAsync(cancellationToken);
        await db.EnsureReceiptContactColumnsAsync(cancellationToken);
        await db.EnsureEan8SkuBarcodesAsync(cancellationToken);
    }
}
