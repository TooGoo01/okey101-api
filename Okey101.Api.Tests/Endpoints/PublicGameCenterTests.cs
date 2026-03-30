using Microsoft.EntityFrameworkCore;
using Okey101.Api.Data;
using Okey101.Api.Models.Entities;
using Okey101.Api.Services;

namespace Okey101.Api.Tests.Endpoints;

public class PublicGameCenterTests : IDisposable
{
    private readonly AppDbContext _dbContext;

    public PublicGameCenterTests()
    {
        var tenantProvider = new TenantProvider();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new AppDbContext(options, tenantProvider);
    }

    private async Task<GameCenter> SeedGameCenter(string name, string location = "Test Location", bool isActive = true)
    {
        var center = new GameCenter
        {
            Id = Guid.NewGuid(),
            Name = name,
            Location = location,
            IsActive = isActive,
            MaxTables = 10
        };
        _dbContext.GameCenters.Add(center);
        await _dbContext.SaveChangesAsync();
        return center;
    }

    // --- InvokeX helper mirroring public endpoint logic ---

    private async Task<List<PublicGameCenterResult>> InvokeGetPublicGameCenters()
    {
        return await _dbContext.GameCenters
            .Where(gc => gc.IsActive)
            .OrderBy(gc => gc.Name)
            .Select(gc => new PublicGameCenterResult
            {
                Id = gc.Id,
                Name = gc.Name,
                Location = gc.Location
            })
            .ToListAsync();
    }

    // --- Tests ---

    [Fact]
    public async Task GetPublicGameCenters_ReturnsOnlyActiveCenters()
    {
        await SeedGameCenter("Active Center A", "Location A", isActive: true);
        await SeedGameCenter("Active Center B", "Location B", isActive: true);
        await SeedGameCenter("Inactive Center", "Location C", isActive: false);

        var results = await InvokeGetPublicGameCenters();

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.DoesNotContain("Inactive", r.Name));
    }

    [Fact]
    public async Task GetPublicGameCenters_ReturnsCentersOrderedByName()
    {
        await SeedGameCenter("Zebra Center");
        await SeedGameCenter("Alpha Center");
        await SeedGameCenter("Middle Center");

        var results = await InvokeGetPublicGameCenters();

        Assert.Equal(3, results.Count);
        Assert.Equal("Alpha Center", results[0].Name);
        Assert.Equal("Middle Center", results[1].Name);
        Assert.Equal("Zebra Center", results[2].Name);
    }

    [Fact]
    public async Task GetPublicGameCenters_ReturnsNameAndLocation()
    {
        await SeedGameCenter("Test Center", "Baku, AZ");

        var results = await InvokeGetPublicGameCenters();

        Assert.Single(results);
        Assert.Equal("Test Center", results[0].Name);
        Assert.Equal("Baku, AZ", results[0].Location);
        Assert.NotEqual(Guid.Empty, results[0].Id);
    }

    [Fact]
    public async Task GetPublicGameCenters_ReturnsEmptyListWhenNoActiveCenters()
    {
        await SeedGameCenter("Inactive Only", isActive: false);

        var results = await InvokeGetPublicGameCenters();

        Assert.Empty(results);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}

public class PublicGameCenterResult
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
}
