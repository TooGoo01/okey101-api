using Microsoft.EntityFrameworkCore;
using Okey101.Api.Data;
using Okey101.Api.Endpoints;
using Okey101.Api.Models.Entities;
using Okey101.Api.Services;

namespace Okey101.Api.Tests.Endpoints;

public class PlatformGameCenterTests : IDisposable
{
    private readonly TenantProvider _tenantProvider;
    private readonly AppDbContext _dbContext;

    public PlatformGameCenterTests()
    {
        _tenantProvider = new TenantProvider();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new AppDbContext(options, _tenantProvider);

        // PlatformAdmin has null TenantId — bypasses all tenant filters
        _tenantProvider.SetTenantId(null);
    }

    private async Task<GameCenter> SeedGameCenter(string name, string location = "Test Location", bool isActive = true, int maxTables = 10)
    {
        var center = new GameCenter
        {
            Id = Guid.NewGuid(),
            Name = name,
            Location = location,
            IsActive = isActive,
            MaxTables = maxTables
        };
        _dbContext.GameCenters.Add(center);
        await _dbContext.SaveChangesAsync();
        return center;
    }

    private async Task<(GameCenter? center, bool created, string? errorCode)> InvokeCreateGameCenter(string name, string location, int maxTables)
    {
        if (string.IsNullOrWhiteSpace(name))
            return (null, false, "VALIDATION_ERROR");

        if (string.IsNullOrWhiteSpace(location))
            return (null, false, "VALIDATION_ERROR");

        var duplicateName = await _dbContext.GameCenters
            .AnyAsync(gc => gc.Name.ToLower() == name.Trim().ToLower());

        if (duplicateName)
            return (null, false, "GAME_CENTER_NAME_EXISTS");

        var center = new GameCenter
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Location = location.Trim(),
            IsActive = true,
            MaxTables = maxTables > 0 ? maxTables : 1
        };

        _dbContext.GameCenters.Add(center);
        await _dbContext.SaveChangesAsync();
        return (center, true, null);
    }

    private async Task<(bool found, bool alreadyInactive, string? errorCode)> InvokeDeactivate(Guid id)
    {
        var center = await _dbContext.GameCenters.FirstOrDefaultAsync(gc => gc.Id == id);
        if (center is null)
            return (false, false, "GAME_CENTER_NOT_FOUND");

        if (!center.IsActive)
            return (true, true, "GAME_CENTER_ALREADY_INACTIVE");

        center.IsActive = false;
        center.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        return (true, false, null);
    }

    private async Task<(bool found, bool alreadyActive, string? errorCode)> InvokeActivate(Guid id)
    {
        var center = await _dbContext.GameCenters.FirstOrDefaultAsync(gc => gc.Id == id);
        if (center is null)
            return (false, false, "GAME_CENTER_NOT_FOUND");

        if (center.IsActive)
            return (true, true, "GAME_CENTER_ALREADY_ACTIVE");

        center.IsActive = true;
        center.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        return (true, false, null);
    }

    [Fact]
    public async Task CreateGameCenter_ReturnsNewCenterWithIsActiveTrue()
    {
        var (center, created, errorCode) = await InvokeCreateGameCenter("New Center", "Baku, Azerbaijan", 8);

        Assert.True(created);
        Assert.Null(errorCode);
        Assert.NotNull(center);
        Assert.Equal("New Center", center!.Name);
        Assert.Equal("Baku, Azerbaijan", center.Location);
        Assert.True(center.IsActive);
        Assert.Equal(8, center.MaxTables);
    }

    [Fact]
    public async Task CreateGameCenter_DuplicateNameReturns409()
    {
        await SeedGameCenter("Existing Center");

        var (_, created, errorCode) = await InvokeCreateGameCenter("existing center", "Other Location", 5);

        Assert.False(created);
        Assert.Equal("GAME_CENTER_NAME_EXISTS", errorCode);
    }

    [Fact]
    public async Task Deactivate_SetsIsActiveFalse()
    {
        var center = await SeedGameCenter("Active Center");

        var (found, alreadyInactive, errorCode) = await InvokeDeactivate(center.Id);

        Assert.True(found);
        Assert.False(alreadyInactive);
        Assert.Null(errorCode);

        var updated = await _dbContext.GameCenters.FirstAsync(gc => gc.Id == center.Id);
        Assert.False(updated.IsActive);
        Assert.NotNull(updated.UpdatedAt);
    }

    [Fact]
    public async Task Deactivate_AlreadyInactiveReturns409()
    {
        var center = await SeedGameCenter("Inactive Center", isActive: false);

        var (found, alreadyInactive, errorCode) = await InvokeDeactivate(center.Id);

        Assert.True(found);
        Assert.True(alreadyInactive);
        Assert.Equal("GAME_CENTER_ALREADY_INACTIVE", errorCode);
    }

    [Fact]
    public async Task Activate_SetsIsActiveTrue()
    {
        var center = await SeedGameCenter("Inactive Center", isActive: false);

        var (found, alreadyActive, errorCode) = await InvokeActivate(center.Id);

        Assert.True(found);
        Assert.False(alreadyActive);
        Assert.Null(errorCode);

        var updated = await _dbContext.GameCenters.FirstAsync(gc => gc.Id == center.Id);
        Assert.True(updated.IsActive);
        Assert.NotNull(updated.UpdatedAt);
    }

    [Fact]
    public async Task Activate_AlreadyActiveReturns409()
    {
        var center = await SeedGameCenter("Already Active");

        var (found, alreadyActive, errorCode) = await InvokeActivate(center.Id);

        Assert.True(found);
        Assert.True(alreadyActive);
        Assert.Equal("GAME_CENTER_ALREADY_ACTIVE", errorCode);
    }

    [Fact]
    public async Task ListGameCenters_ReturnsAllCentersActiveAndInactive()
    {
        await SeedGameCenter("Center A", isActive: true);
        await SeedGameCenter("Center B", isActive: false);
        await SeedGameCenter("Center C", isActive: true);

        var allCenters = await _dbContext.GameCenters.OrderBy(gc => gc.Name).ToListAsync();

        Assert.Equal(3, allCenters.Count);
        Assert.Equal("Center A", allCenters[0].Name);
        Assert.True(allCenters[0].IsActive);
        Assert.Equal("Center B", allCenters[1].Name);
        Assert.False(allCenters[1].IsActive);
        Assert.Equal("Center C", allCenters[2].Name);
        Assert.True(allCenters[2].IsActive);
    }

    [Fact]
    public async Task Deactivate_NonExistentReturns404()
    {
        var (found, _, errorCode) = await InvokeDeactivate(Guid.NewGuid());

        Assert.False(found);
        Assert.Equal("GAME_CENTER_NOT_FOUND", errorCode);
    }

    [Fact]
    public async Task Activate_NonExistentReturns404()
    {
        var (found, _, errorCode) = await InvokeActivate(Guid.NewGuid());

        Assert.False(found);
        Assert.Equal("GAME_CENTER_NOT_FOUND", errorCode);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
