using Microsoft.EntityFrameworkCore;
using StoreShared;

namespace StoreAPI.Data;

public class StoreDbContext : DbContext
{
    public StoreDbContext(DbContextOptions<StoreDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<ProductModel> ProductModels => Set<ProductModel>();
    public DbSet<SKU> Skus => Set<SKU>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<SaleEvent> SaleEvents => Set<SaleEvent>();
    public DbSet<SaleEventLine> SaleEventLines => Set<SaleEventLine>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
    public DbSet<StoreSettings> StoreSettings => Set<StoreSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Username).HasMaxLength(128);
            e.Property(x => x.PasswordHash).HasMaxLength(512);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<ProductModel>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.BuyPrice).HasPrecision(18, 2);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.SalePrice).HasPrecision(18, 2);
        });

        modelBuilder.Entity<SKU>(e =>
        {
            e.ToTable("Skus");
            e.HasKey(x => x.Id);
            e.Property(x => x.BuyPrice).HasPrecision(18, 2);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.SalePrice).HasPrecision(18, 2);
            e.Property(x => x.Size).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Barcode).HasMaxLength(8);
            e.HasIndex(x => x.Barcode).IsUnique();
            e.HasIndex(x => new { x.ProductModelId, x.Size }).IsUnique();
            e.HasOne(x => x.ProductModel)
                .WithMany(p => p.Skus)
                .HasForeignKey(x => x.ProductModelId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Sale>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ReceiptNumber).HasMaxLength(32);
            e.HasIndex(x => x.ReceiptNumber).IsUnique();
            e.Property(x => x.IsFullyRefunded).HasDefaultValue(false);
            // SQLite: DateTimeOffset range filters are not translated; store UTC DateTime instead.
            e.Property(x => x.Timestamp).HasConversion(
                v => v.UtcDateTime,
                v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
            e.Property(x => x.TotalAmount).HasPrecision(18, 2);
            e.Property(x => x.DiscountAmount).HasPrecision(18, 2);
            e.HasOne(x => x.User)
                .WithMany(u => u.Sales)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SaleEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ReceiptNumber).HasMaxLength(32);
            e.Property(x => x.RefundReceiptNumber).HasMaxLength(40);
            e.Property(x => x.EventType).HasMaxLength(32);
            e.Property(x => x.PerformedBy).HasMaxLength(128);
            e.Property(x => x.Note).HasMaxLength(2000);
            e.Property(x => x.AmountAffected).HasPrecision(18, 2);
            e.Property(x => x.Timestamp).HasConversion(
                v => v.UtcDateTime,
                v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
            e.HasIndex(x => x.ReceiptNumber);
            e.HasIndex(x => x.Timestamp);
            e.HasOne(x => x.Sale)
                .WithMany(s => s.Events)
                .HasForeignKey(x => x.SaleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SaleEventLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ProductName).HasMaxLength(256);
            e.Property(x => x.Size).HasMaxLength(32);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.UnitCost).HasPrecision(18, 2);
            e.Property(x => x.LineTotal).HasPrecision(18, 2);
            e.HasOne(x => x.SaleEvent)
                .WithMany(ev => ev.Lines)
                .HasForeignKey(x => x.SaleEventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StockAdjustment>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Reason).HasMaxLength(512);
            e.Property(x => x.PerformedBy).HasMaxLength(128);
            e.Property(x => x.Timestamp).HasConversion(
                v => v.UtcDateTime,
                v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
            e.HasIndex(x => x.Timestamp);
            e.HasOne(x => x.Sku)
                .WithMany()
                .HasForeignKey(x => x.SkuId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SaleEvent)
                .WithMany(ev => ev.StockAdjustments)
                .HasForeignKey(x => x.SaleEventId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SaleItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.UnitCost).HasPrecision(18, 2);
            e.HasOne(x => x.Sale)
                .WithMany(s => s.Items)
                .HasForeignKey(x => x.SaleId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.SKU)
                .WithMany(s => s.SaleItems)
                .HasForeignKey(x => x.SKUId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StoreSettings>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.StoreName).HasMaxLength(256);
            e.Property(x => x.CurrencyLabel).HasMaxLength(32);
            e.Property(x => x.ReceiptAddress).HasMaxLength(512);
            e.Property(x => x.ReceiptLandline).HasMaxLength(64);
            e.Property(x => x.ReceiptPhone).HasMaxLength(64);
            e.Property(x => x.DiscordBackupWebhookUrl).HasMaxLength(2048);
        });
    }
}
