using Microsoft.EntityFrameworkCore;
using Okey101.Api.Configuration;
using Okey101.Api.Models.Entities;
using Okey101.Api.Models.Enums;
using Okey101.Api.Services;

namespace Okey101.Api.Data;

public static class DataSeeder
{
    public static async Task SeedEssentialDataAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantProvider = scope.ServiceProvider.GetRequiredService<ITenantProvider>();
        tenantProvider.SetTenantId(null);

        var centerId = Guid.Parse(AuthConfiguration.DevTenantId);

        // Ensure game center exists and update name
        var center = await db.GameCenters.IgnoreQueryFilters()
            .FirstOrDefaultAsync(gc => gc.Id == centerId);
        if (center is null)
        {
            db.GameCenters.Add(new GameCenter
            {
                Id = centerId,
                Name = "Badam",
                Location = "Bakı, Azərbaycan",
                IsActive = true,
                MaxTables = 20
            });
        }
        else
        {
            center.Name = "Badam";
            center.Location = "Bakı, Azərbaycan";
        }
        await db.SaveChangesAsync();

        // Ensure 6 tables exist (TABLE-1 .. TABLE-6)
        for (var i = 1; i <= 6; i++)
        {
            var qr = $"TABLE-{i}";
            var exists = await db.Tables.IgnoreQueryFilters()
                .AnyAsync(t => t.QrCodeIdentifier == qr && t.GameCenterId == centerId);
            if (!exists)
            {
                db.Tables.Add(new Table
                {
                    Id = Guid.NewGuid(),
                    TenantId = centerId,
                    TableNumber = i,
                    Status = TableStatus.Active,
                    QrCodeIdentifier = qr,
                    GameCenterId = centerId
                });
            }
            else
            {
                // Reactivate if closed
                var table = await db.Tables.IgnoreQueryFilters()
                    .FirstAsync(t => t.QrCodeIdentifier == qr && t.GameCenterId == centerId);
                if (table.Status == TableStatus.Closed)
                    table.Status = TableStatus.Active;
            }
        }
        await db.SaveChangesAsync();

        // Seed default admin players (runs in all environments)
        var phoneEncryption = scope.ServiceProvider.GetRequiredService<IPhoneEncryptionService>();
        await SeedAdminPlayer(db, phoneEncryption, centerId,
            "+994515262222", "Admin", Guid.Parse(AuthConfiguration.DevPlayerId), UserRole.PlatformAdmin);
        await SeedAdminPlayer(db, phoneEncryption, centerId,
            "+905551000001", "Admin Two");

        // Set username/password on the admin player
        await db.SaveChangesAsync(); // Save pending player inserts first
        var adminPlayer = await db.Players.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == Guid.Parse(AuthConfiguration.DevPlayerId));
        if (adminPlayer is not null && adminPlayer.Username is null)
        {
            adminPlayer.Username = "admin";
            adminPlayer.PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin1234");
            await db.SaveChangesAsync();
        }
    }

    public static async Task SeedDevelopmentDataAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var phoneEncryption = scope.ServiceProvider.GetRequiredService<IPhoneEncryptionService>();
        var tenantProvider = scope.ServiceProvider.GetRequiredService<ITenantProvider>();

        // Bypass tenant filter for seeding
        tenantProvider.SetTenantId(null);

        // Ensure a game center exists
        var gameCenterId = Guid.Parse(AuthConfiguration.DevTenantId);
        var existingCenter = await db.GameCenters
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(gc => gc.Id == gameCenterId);

        if (existingCenter is null)
        {
            db.GameCenters.Add(new GameCenter
            {
                Id = gameCenterId,
                Name = "Dev Game Center",
                Location = "Baku, Azerbaijan",
                IsActive = true,
                MaxTables = 10
            });
            await db.SaveChangesAsync();
        }

        // Seed the dev admin player with a well-known ID (matches dev-skip-token bypass)
        var devPlayerId = Guid.Parse(AuthConfiguration.DevPlayerId);
        await SeedAdminPlayer(db, phoneEncryption, gameCenterId,
            "+905551000001", "Admin One", devPlayerId, UserRole.PlatformAdmin);
        await SeedAdminPlayer(db, phoneEncryption, gameCenterId,
            "+905551000002", "Admin Two");

        // Seed a dev table so QR scan flow works immediately
        var tableId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var existingTable = await db.Tables
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tableId);

        if (existingTable is null)
        {
            db.Tables.Add(new Table
            {
                Id = tableId,
                TenantId = gameCenterId,
                TableNumber = 1,
                Status = TableStatus.Active,
                QrCodeIdentifier = "DEV-TABLE-1",
                GameCenterId = gameCenterId
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedAdminPlayer(
        AppDbContext db,
        IPhoneEncryptionService phoneEncryption,
        Guid gameCenterId,
        string phone,
        string name,
        Guid? fixedId = null,
        UserRole role = UserRole.GameCenterAdmin)
    {
        if (fixedId.HasValue)
        {
            var existingById = await db.Players
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.Id == fixedId.Value);

            if (existingById is null)
            {
                var phoneHash = phoneEncryption.Hash(phone);
                db.Players.Add(new Player
                {
                    Id = fixedId.Value,
                    Name = name,
                    PhoneNumber = phoneEncryption.Encrypt(phone),
                    PhoneNumberHash = phoneHash,
                    Role = role,
                    TenantId = gameCenterId
                });
            }
            else
            {
                existingById.Role = role;
                existingById.TenantId ??= gameCenterId;
            }
            return;
        }

        var hash = phoneEncryption.Hash(phone);
        var existing = await db.Players
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.PhoneNumberHash == hash);

        if (existing is null)
        {
            db.Players.Add(new Player
            {
                Id = Guid.NewGuid(),
                Name = name,
                PhoneNumber = phoneEncryption.Encrypt(phone),
                PhoneNumberHash = hash,
                Role = role,
                TenantId = gameCenterId
            });
        }
        else
        {
            existing.Role = role;
            existing.TenantId ??= gameCenterId;
        }
    }

}
