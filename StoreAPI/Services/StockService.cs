using Microsoft.EntityFrameworkCore;
using StoreAPI.Data;
using StoreShared;

namespace StoreAPI.Services;

public sealed class StockService
{
    private readonly StoreDbContext _db;

    public StockService(StoreDbContext db)
    {
        _db = db;
    }

    public async Task<(bool Ok, string? Error)> TrySubtractAsync(int skuId, int quantity, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
            return (false, "Quantity must be greater than zero.");

        var sku = await _db.Skus.FirstOrDefaultAsync(s => s.Id == skuId, cancellationToken);
        if (sku is null)
            return (false, "SKU not found.");

        if (sku.Stock == 0)
            return (false, $"This item is out of stock (barcode {sku.Barcode}).");

        if (sku.Stock < quantity)
            return (false, $"Insufficient stock for this item (SKU #{skuId}, barcode {sku.Barcode}). Available: {sku.Stock}, requested: {quantity}.");

        sku.Stock -= quantity;
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> TryAddAsync(int skuId, int quantity, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
            return (false, "Quantity must be greater than zero.");

        var sku = await _db.Skus.FirstOrDefaultAsync(s => s.Id == skuId, cancellationToken);
        if (sku is null)
            return (false, "SKU not found.");

        sku.Stock += quantity;
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> SetStockAsync(int skuId, int quantity, CancellationToken cancellationToken = default)
    {
        if (quantity < 0)
            return (false, "Stock cannot be negative.");

        var sku = await _db.Skus.FirstOrDefaultAsync(s => s.Id == skuId, cancellationToken);
        if (sku is null)
            return (false, "SKU not found.");

        sku.Stock = quantity;
        return (true, null);
    }
}
