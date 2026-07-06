using System.Globalization;
using Microsoft.EntityFrameworkCore;
using StoreShared;
using StoreShared.Barcode;

namespace StoreAPI.Data;

/// <summary>
/// EnsuresCreated does not alter existing SQLite files. Patch columns added after first deploy.
/// </summary>
public static class SqliteSchemaExtensions
{
    public static async Task EnsurePhase9SchemaAsync(this StoreDbContext db, CancellationToken cancellationToken = default)
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(cancellationToken);
        try
        {
            await using (var check = conn.CreateCommand())
            {
                check.CommandText = """SELECT COUNT(*) FROM pragma_table_info('Users') WHERE name='IsActive';""";
                var n = Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
                if (n == 0)
                {
                    await using var alter = conn.CreateCommand();
                    alter.CommandText = """ALTER TABLE "Users" ADD COLUMN "IsActive" INTEGER NOT NULL DEFAULT 1;""";
                    await alter.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            await using (var checkTable = conn.CreateCommand())
            {
                checkTable.CommandText =
                    """SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='StoreSettings';""";
                var exists = Convert.ToInt64(await checkTable.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0;
                if (!exists)
                {
                    await using var create = conn.CreateCommand();
                    create.CommandText = """
                        CREATE TABLE "StoreSettings" (
                          "Id" INTEGER NOT NULL PRIMARY KEY,
                          "StoreName" TEXT NOT NULL,
                          "CurrencyLabel" TEXT NOT NULL,
                          "LowStockThreshold" INTEGER NOT NULL,
                          "AllowSellerDiscount" INTEGER NOT NULL
                        );
                        INSERT INTO "StoreSettings" ("Id", "StoreName", "CurrencyLabel", "LowStockThreshold", "AllowSellerDiscount")
                        VALUES (1, 'Kaif', 'EGP', 5, 0);
                        """;
                    await create.ExecuteNonQueryAsync(cancellationToken);
                }
            }
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    public static async Task EnsureBackupSettingsColumnsAsync(this StoreDbContext db, CancellationToken cancellationToken = default)
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(cancellationToken);
        try
        {
            await EnsureColumnAsync(conn, "StoreSettings", "DiscordBackupWebhookUrl",
                """ALTER TABLE "StoreSettings" ADD COLUMN "DiscordBackupWebhookUrl" TEXT NOT NULL DEFAULT '';""",
                cancellationToken);
            await EnsureColumnAsync(conn, "StoreSettings", "BackupIntervalHours",
                """ALTER TABLE "StoreSettings" ADD COLUMN "BackupIntervalHours" INTEGER NOT NULL DEFAULT 24;""",
                cancellationToken);
            await EnsureColumnAsync(conn, "StoreSettings", "LastBackupUtc",
                """ALTER TABLE "StoreSettings" ADD COLUMN "LastBackupUtc" TEXT NULL;""",
                cancellationToken);
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    private static async Task EnsureColumnAsync(
        System.Data.Common.DbConnection conn,
        string table,
        string column,
        string alterSql,
        CancellationToken cancellationToken)
    {
        await using var check = conn.CreateCommand();
        check.CommandText = $"""SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}';""";
        var n = Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (n > 0)
            return;
        await using var alter = conn.CreateCommand();
        alter.CommandText = alterSql;
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Reassign legacy Code128 / EAN-13 text barcodes to compact EAN-8 codes from SKU id.</summary>
    public static async Task<int> EnsureEan8SkuBarcodesAsync(this StoreDbContext db, CancellationToken cancellationToken = default)
    {
        var skus = await db.Skus.ToListAsync(cancellationToken);
        var updated = 0;
        foreach (var sku in skus)
        {
            var expected = SkuBarcode.ForSkuId(sku.Id);
            if (sku.Barcode == expected)
                continue;
            sku.Barcode = expected;
            updated++;
        }

        if (updated > 0)
            await db.SaveChangesAsync(cancellationToken);

        return updated;
    }

    /// <summary>Ensure every product has one primary SKU; merge stock from legacy multi-size rows onto the primary.</summary>
    public static async Task EnsurePrimarySkuPerProductAsync(this StoreDbContext db, CancellationToken cancellationToken = default)
    {
        var models = await db.ProductModels.Include(p => p.Skus).ToListAsync(cancellationToken);
        var changed = false;

        foreach (var model in models)
        {
            if (model.Skus.Count == 0)
            {
                var sku = new SKU
                {
                    ProductModelId = model.Id,
                    Size = ClothingSize.Custom,
                    Stock = 0,
                };
                db.Skus.Add(sku);
                await db.SaveChangesAsync(cancellationToken);
                sku.Barcode = SkuBarcode.ForSkuId(sku.Id);
                changed = true;
                continue;
            }

            if (model.Skus.Count <= 1)
                continue;

            var primary = model.Skus.OrderBy(s => s.Id).First();
            var extraStock = model.Skus.Where(s => s.Id != primary.Id).Sum(s => s.Stock);
            if (extraStock > 0)
            {
                primary.Stock += extraStock;
                foreach (var extra in model.Skus.Where(s => s.Id != primary.Id))
                    extra.Stock = 0;
                changed = true;
            }
        }

        if (changed)
            await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task EnsureSaleEventsSchemaAsync(this StoreDbContext db, CancellationToken cancellationToken = default)
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(cancellationToken);
        try
        {
            await EnsureColumnAsync(conn, "Sales", "ReceiptNumber",
                """ALTER TABLE "Sales" ADD COLUMN "ReceiptNumber" TEXT NOT NULL DEFAULT '';""",
                cancellationToken);
            await EnsureColumnAsync(conn, "Sales", "IsFullyRefunded",
                """ALTER TABLE "Sales" ADD COLUMN "IsFullyRefunded" INTEGER NOT NULL DEFAULT 0;""",
                cancellationToken);

            await using (var check = conn.CreateCommand())
            {
                check.CommandText =
                    """SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='SaleEvents';""";
                var exists = Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0;
                if (!exists)
                {
                    await using var create = conn.CreateCommand();
                    create.CommandText = """
                        CREATE TABLE "SaleEvents" (
                          "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                          "SaleId" INTEGER NOT NULL,
                          "ReceiptNumber" TEXT NOT NULL,
                          "EventType" TEXT NOT NULL,
                          "Timestamp" TEXT NOT NULL,
                          "PerformedBy" TEXT NOT NULL,
                          "Note" TEXT NULL,
                          "AmountAffected" TEXT NULL,
                          "RefundReceiptNumber" TEXT NULL,
                          FOREIGN KEY ("SaleId") REFERENCES "Sales" ("Id") ON DELETE CASCADE
                        );
                        CREATE INDEX "IX_SaleEvents_ReceiptNumber" ON "SaleEvents" ("ReceiptNumber");
                        CREATE INDEX "IX_SaleEvents_Timestamp" ON "SaleEvents" ("Timestamp");
                        CREATE TABLE "SaleEventLines" (
                          "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                          "SaleEventId" INTEGER NOT NULL,
                          "SkuId" INTEGER NOT NULL,
                          "ProductName" TEXT NOT NULL,
                          "Size" TEXT NOT NULL,
                          "Quantity" INTEGER NOT NULL,
                          "UnitPrice" TEXT NOT NULL,
                          "LineTotal" TEXT NOT NULL,
                          FOREIGN KEY ("SaleEventId") REFERENCES "SaleEvents" ("Id") ON DELETE CASCADE
                        );
                        CREATE TABLE "StockAdjustments" (
                          "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                          "SkuId" INTEGER NOT NULL,
                          "QuantityDelta" INTEGER NOT NULL,
                          "Reason" TEXT NOT NULL,
                          "SaleEventId" INTEGER NULL,
                          "Timestamp" TEXT NOT NULL,
                          "PerformedBy" TEXT NOT NULL,
                          FOREIGN KEY ("SkuId") REFERENCES "Skus" ("Id") ON DELETE RESTRICT,
                          FOREIGN KEY ("SaleEventId") REFERENCES "SaleEvents" ("Id") ON DELETE SET NULL
                        );
                        CREATE INDEX "IX_StockAdjustments_Timestamp" ON "StockAdjustments" ("Timestamp");
                        """;
                    await create.ExecuteNonQueryAsync(cancellationToken);
                }
            }
        }
        finally
        {
            await conn.CloseAsync();
        }

        var sales = await db.Sales.Where(s => s.ReceiptNumber == "" || s.ReceiptNumber == null).ToListAsync(cancellationToken);
        foreach (var sale in sales)
            sale.ReceiptNumber = StoreShared.Sales.ReceiptNumberFormat.ForSale(sale.Id);

        if (sales.Count > 0)
            await db.SaveChangesAsync(cancellationToken);

        var saleIdsWithCompleted = await db.SaleEvents
            .Where(e => e.EventType == nameof(StoreShared.Sales.SaleEventType.Completed))
            .Select(e => e.SaleId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var missingCompleted = await db.Sales
            .Include(s => s.User)
            .Include(s => s.Items).ThenInclude(i => i.SKU!).ThenInclude(sku => sku.ProductModel)
            .Where(s => !saleIdsWithCompleted.Contains(s.Id))
            .ToListAsync(cancellationToken);

        foreach (var sale in missingCompleted)
        {
            var lines = sale.Items.Select(i => new SaleEventLine
            {
                SkuId = i.SKUId,
                ProductName = i.SKU?.ProductModel?.Name ?? "—",
                Size = i.SKU?.Size.ToString() ?? "",
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                LineTotal = i.UnitPrice * i.Quantity,
            }).ToList();

            db.SaleEvents.Add(new SaleEvent
            {
                SaleId = sale.Id,
                ReceiptNumber = sale.ReceiptNumber,
                EventType = nameof(StoreShared.Sales.SaleEventType.Completed),
                Timestamp = sale.Timestamp,
                PerformedBy = sale.User?.Username ?? "system",
                AmountAffected = sale.TotalAmount,
                Lines = lines,
            });
        }

        if (missingCompleted.Count > 0)
            await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task EnsureReceiptContactColumnsAsync(this StoreDbContext db, CancellationToken cancellationToken = default)
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(cancellationToken);
        try
        {
            await EnsureColumnAsync(conn, "StoreSettings", "ReceiptLandline",
                """ALTER TABLE "StoreSettings" ADD COLUMN "ReceiptLandline" TEXT NOT NULL DEFAULT '';""",
                cancellationToken);
            await EnsureColumnAsync(conn, "StoreSettings", "ReceiptPhone",
                """ALTER TABLE "StoreSettings" ADD COLUMN "ReceiptPhone" TEXT NOT NULL DEFAULT '';""",
                cancellationToken);
            await EnsureColumnAsync(conn, "StoreSettings", "ReceiptAddress",
                """ALTER TABLE "StoreSettings" ADD COLUMN "ReceiptAddress" TEXT NOT NULL DEFAULT '';""",
                cancellationToken);
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    public static async Task EnsureSkuUnitPriceColumnAsync(this StoreDbContext db, CancellationToken cancellationToken = default)
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(cancellationToken);
        try
        {
            await using (var check = conn.CreateCommand())
            {
                check.CommandText = """SELECT COUNT(*) FROM pragma_table_info('Skus') WHERE name='UnitPrice';""";
                var n = Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
                if (n > 0)
                    return;
            }

            await using (var alter = conn.CreateCommand())
            {
                // Match EF Core SQLite decimal mapping (TEXT affinity with numeric string).
                alter.CommandText = """ALTER TABLE "Skus" ADD COLUMN "UnitPrice" TEXT NOT NULL DEFAULT '0';""";
                await alter.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    public static async Task EnsureSkuSalePriceColumnAsync(this StoreDbContext db, CancellationToken cancellationToken = default)
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(cancellationToken);
        try
        {
            await using (var check = conn.CreateCommand())
            {
                check.CommandText = """SELECT COUNT(*) FROM pragma_table_info('Skus') WHERE name='SalePrice';""";
                var n = Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
                if (n > 0)
                    return;
            }

            await using (var alter = conn.CreateCommand())
            {
                alter.CommandText = """ALTER TABLE "Skus" ADD COLUMN "SalePrice" TEXT NULL;""";
                await alter.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    public static async Task EnsureProductModelPricingColumnsAsync(this StoreDbContext db, CancellationToken cancellationToken = default)
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(cancellationToken);
        try
        {
            await EnsureColumnAsync(conn, "ProductModels", "BuyPrice",
                """ALTER TABLE "ProductModels" ADD COLUMN "BuyPrice" TEXT NOT NULL DEFAULT '0';""",
                cancellationToken);
            await EnsureColumnAsync(conn, "ProductModels", "UnitPrice",
                """ALTER TABLE "ProductModels" ADD COLUMN "UnitPrice" TEXT NOT NULL DEFAULT '0';""",
                cancellationToken);
            await EnsureColumnAsync(conn, "ProductModels", "SalePrice",
                """ALTER TABLE "ProductModels" ADD COLUMN "SalePrice" TEXT NULL;""",
                cancellationToken);
        }
        finally
        {
            await conn.CloseAsync();
        }

        var models = await db.ProductModels.Include(p => p.Skus).ToListAsync(cancellationToken);
        var changed = false;
        foreach (var model in models)
        {
            if (model.UnitPrice > 0 || model.BuyPrice > 0)
                continue;

            var sku = model.Skus.OrderBy(s => s.Id).FirstOrDefault();
            if (sku is null || (sku.UnitPrice <= 0 && sku.BuyPrice <= 0))
                continue;

            model.BuyPrice = sku.BuyPrice < 0 ? 0 : sku.BuyPrice;
            model.UnitPrice = sku.UnitPrice < 0 ? 0 : sku.UnitPrice;
            model.SalePrice = sku.SalePrice is { } sp && sp > 0 ? sp : null;
            changed = true;
        }

        if (changed)
            await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task EnsureSkuBuyPriceColumnAsync(this StoreDbContext db, CancellationToken cancellationToken = default)
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(cancellationToken);
        try
        {
            await using (var check = conn.CreateCommand())
            {
                check.CommandText = """SELECT COUNT(*) FROM pragma_table_info('Skus') WHERE name='BuyPrice';""";
                var n = Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
                if (n > 0)
                    return;
            }

            await using (var alter = conn.CreateCommand())
            {
                alter.CommandText = """ALTER TABLE "Skus" ADD COLUMN "BuyPrice" TEXT NOT NULL DEFAULT '0';""";
                await alter.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    public static async Task EnsureSaleItemUnitCostColumnAsync(this StoreDbContext db, CancellationToken cancellationToken = default)
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(cancellationToken);
        try
        {
            await using (var check = conn.CreateCommand())
            {
                check.CommandText = """SELECT COUNT(*) FROM pragma_table_info('SaleItems') WHERE name='UnitCost';""";
                var n = Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
                if (n > 0)
                    return;
            }

            await using (var alter = conn.CreateCommand())
            {
                alter.CommandText = """ALTER TABLE "SaleItems" ADD COLUMN "UnitCost" TEXT NOT NULL DEFAULT '0';""";
                await alter.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    public static async Task EnsureSaleEventLineUnitCostColumnAsync(this StoreDbContext db, CancellationToken cancellationToken = default)
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(cancellationToken);
        try
        {
            await using (var check = conn.CreateCommand())
            {
                check.CommandText = """SELECT COUNT(*) FROM pragma_table_info('SaleEventLines') WHERE name='UnitCost';""";
                var n = Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
                if (n > 0)
                    return;
            }

            await using (var alter = conn.CreateCommand())
            {
                alter.CommandText = """ALTER TABLE "SaleEventLines" ADD COLUMN "UnitCost" TEXT NOT NULL DEFAULT '0';""";
                await alter.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            await conn.CloseAsync();
        }
    }
}
