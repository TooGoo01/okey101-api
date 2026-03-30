using Microsoft.EntityFrameworkCore;
using Okey101.Api.Data;
using Okey101.Api.Models.Entities;
using Okey101.Api.Models.Enums;
using Okey101.Api.Services;

namespace Okey101.Api.Tests.Data;

public class AppDbContextTests : IDisposable
{
    private readonly TenantProvider _tenantProvider;
    private readonly AppDbContext _dbContext;

    public AppDbContextTests()
    {
        _tenantProvider = new TenantProvider();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new AppDbContext(options, _tenantProvider);
    }

    [Fact]
    public async Task TenantFilter_WhenTenantSet_FiltersPlayers()
    {
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();

        _tenantProvider.SetTenantId(null); // superadmin to seed data
        _dbContext.Players.AddRange(
            new Player { Id = Guid.NewGuid(), Name = "Player1", PhoneNumber = "enc1", PhoneNumberHash = "h1", TenantId = tenant1 },
            new Player { Id = Guid.NewGuid(), Name = "Player2", PhoneNumber = "enc2", PhoneNumberHash = "h2", TenantId = tenant2 },
            new Player { Id = Guid.NewGuid(), Name = "Player3", PhoneNumber = "enc3", PhoneNumberHash = "h3", TenantId = tenant1 }
        );
        await _dbContext.SaveChangesAsync();

        // Now query as tenant1
        _tenantProvider.SetTenantId(tenant1);
        var players = await _dbContext.Players.ToListAsync();

        Assert.Equal(2, players.Count);
        Assert.All(players, p => Assert.Equal(tenant1, p.TenantId));
    }

    [Fact]
    public async Task TenantFilter_WhenNullTenant_ReturnAllPlayers()
    {
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();

        _tenantProvider.SetTenantId(null); // superadmin
        _dbContext.Players.AddRange(
            new Player { Id = Guid.NewGuid(), Name = "P1", PhoneNumber = "e1", PhoneNumberHash = "h1", TenantId = tenant1 },
            new Player { Id = Guid.NewGuid(), Name = "P2", PhoneNumber = "e2", PhoneNumberHash = "h2", TenantId = tenant2 }
        );
        await _dbContext.SaveChangesAsync();

        // Superadmin (null tenant) sees all
        var players = await _dbContext.Players.ToListAsync();

        Assert.Equal(2, players.Count);
    }

    [Fact]
    public async Task PlayerEntity_RequiredFieldsConfigured()
    {
        _tenantProvider.SetTenantId(null);

        var player = new Player
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            PhoneNumber = "encrypted_phone",
            PhoneNumberHash = "hash_value",
            Role = UserRole.GameCenterAdmin,
            TenantId = Guid.NewGuid()
        };

        _dbContext.Players.Add(player);
        await _dbContext.SaveChangesAsync();

        var loaded = await _dbContext.Players.FindAsync(player.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Test", loaded.Name);
        Assert.Equal(UserRole.GameCenterAdmin, loaded.Role);
    }

    [Fact]
    public async Task RefreshToken_CascadeDeletesWithPlayer()
    {
        _tenantProvider.SetTenantId(null);

        var player = new Player
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            PhoneNumber = "enc",
            PhoneNumberHash = "hash"
        };
        _dbContext.Players.Add(player);

        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            Token = "test-token",
            PlayerId = player.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        };
        _dbContext.RefreshTokens.Add(token);
        await _dbContext.SaveChangesAsync();

        _dbContext.Players.Remove(player);
        await _dbContext.SaveChangesAsync();

        var tokens = await _dbContext.RefreshTokens.ToListAsync();
        Assert.Empty(tokens);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
