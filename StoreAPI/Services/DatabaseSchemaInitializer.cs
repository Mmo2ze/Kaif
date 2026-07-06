using Microsoft.EntityFrameworkCore;
using StoreAPI.Data;
using StoreShared;

namespace StoreAPI.Services;

/// <summary>Applies SQLite schema patches after startup or database restore.</summary>
public static class DatabaseSchemaInitializer
{
    public static async Task ApplyAsync(StoreDbContext db, CancellationToken cancellationToken = default)
    {
        var dbPath = ParseDataSourcePath(db.Database.GetDbConnection().ConnectionString);
        if (dbPath is not null)
            StoreDataPaths.RemoveCorruptDatabaseIfNeeded(dbPath);

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
        await db.EnsurePrimarySkuPerProductAsync(cancellationToken);
    }

    private static string? ParseDataSourcePath(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return null;

        const string prefix = "Data Source=";
        var idx = connectionString.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;

        return connectionString[(idx + prefix.Length)..].Split(';', 2)[0].Trim();
    }
}
