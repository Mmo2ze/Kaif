using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoreAPI.Data;
using StoreShared.Pos;

namespace StoreAPI.Services;

/// <summary>SQLite-backed store settings kept in memory and refreshed after updates.</summary>
public sealed class StoreRuntimeSettings
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Lock _lock = new();

    public StoreRuntimeSettings(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public string StoreName { get; private set; } = "Kaif";

    public string CurrencyLabel { get; private set; } = "EGP";

    public int LowStockThreshold { get; private set; } = 5;

    public bool AllowSellerDiscount { get; private set; }

    public string ReceiptLandline { get; private set; } = "";

    public string ReceiptPhone { get; private set; } = "";

    public PosSettingsDto ToDto()
    {
        lock (_lock)
            return new PosSettingsDto(
                StoreName,
                CurrencyLabel,
                AllowSellerDiscount,
                LowStockThreshold,
                string.IsNullOrWhiteSpace(ReceiptLandline) ? null : ReceiptLandline,
                string.IsNullOrWhiteSpace(ReceiptPhone) ? null : ReceiptPhone);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
        var row = await db.StoreSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (row is null)
            return;

        lock (_lock)
        {
            StoreName = row.StoreName;
            CurrencyLabel = row.CurrencyLabel;
            LowStockThreshold = row.LowStockThreshold;
            AllowSellerDiscount = row.AllowSellerDiscount;
            ReceiptLandline = row.ReceiptLandline ?? "";
            ReceiptPhone = row.ReceiptPhone ?? "";
        }
    }
}
