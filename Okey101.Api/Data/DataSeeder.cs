using Microsoft.EntityFrameworkCore;
using Okey101.Api.Configuration;
using Okey101.Api.Models.Entities;
using Okey101.Api.Models.Enums;
using Okey101.Api.Services;

namespace Okey101.Api.Data;

public static class DataSeeder
{
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
            "+905551000001", "Admin One", devPlayerId);
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
        Guid? fixedId = null)
    {
        var phoneHash = phoneEncryption.Hash(phone);
        var exists = await db.Players
            .IgnoreQueryFilters()
            .AnyAsync(p => p.PhoneNumberHash == phoneHash);

        if (!exists)
        {
            db.Players.Add(new Player
            {
                Id = fixedId ?? Guid.NewGuid(),
                Name = name,
                PhoneNumber = phoneEncryption.Encrypt(phone),
                PhoneNumberHash = phoneHash,
                Role = UserRole.GameCenterAdmin,
                TenantId = gameCenterId
            });
        }
    }
}
