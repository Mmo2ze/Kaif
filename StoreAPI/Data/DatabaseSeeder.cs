using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StoreShared;

namespace StoreAPI.Data;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(StoreDbContext db, CancellationToken cancellationToken = default)
    {
        if (!await db.Users.AnyAsync(cancellationToken))
        {
            var hasher = new PasswordHasher<User>();

            var admin = new User { Username = "admin", Role = UserRole.Admin };
            admin.PasswordHash = hasher.HashPassword(admin, "Admin123!");

            var seller = new User { Username = "seller", Role = UserRole.Seller };
            seller.PasswordHash = hasher.HashPassword(seller, "Seller123!");

            db.Users.AddRange(admin, seller);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (!await db.StoreSettings.AnyAsync(cancellationToken))
        {
            db.StoreSettings.Add(new StoreSettings { Id = 1 });
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
