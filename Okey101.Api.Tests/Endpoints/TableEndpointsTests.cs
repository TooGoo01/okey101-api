using Microsoft.EntityFrameworkCore;
using Okey101.Api.Data;
using Okey101.Api.Models.Entities;
using Okey101.Api.Models.Enums;
using Okey101.Api.Services;

namespace Okey101.Api.Tests.Endpoints;

public class TableEndpointsTests : IDisposable
{
    private readonly TenantProvider _tenantProvider;
    private readonly AppDbContext _dbContext;
    private readonly Guid _gameCenterId;
    private readonly Guid _tenantId;

    public TableEndpointsTests()
    {
        _tenantProvider = new TenantProvider();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new AppDbContext(options, _tenantProvider);
        _gameCenterId = Guid.NewGuid();
        _tenantId = Guid.NewGuid();

        // Seed a game center
        _tenantProvider.SetTenantId(null);
        _dbContext.GameCenters.Add(new GameCenter
        {
            Id = _gameCenterId,
            Name = "Test Game Center",
            IsActive = true,
            MaxTables = 10
        });
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task Resolve_ValidQrCode_ReturnsTableInfo()
    {
        var table = new Table
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            TableNumber = 7,
            Status = TableStatus.Active,
            QrCodeIdentifier = "tc-table-7",
            GameCenterId = _gameCenterId
        };
        _dbContext.Tables.Add(table);
        await _dbContext.SaveChangesAsync();

        var resolved = await _dbContext.Tables
            .IgnoreQueryFilters()
            .Include(t => t.GameCenter)
            .FirstOrDefaultAsync(t => t.QrCodeIdentifier == "tc-table-7");

        Assert.NotNull(resolved);
        Assert.Equal(7, resolved.TableNumber);
        Assert.Equal("Test Game Center", resolved.GameCenter.Name);
        Assert.Equal(TableStatus.Active, resolved.Status);
    }

    [Fact]
    public async Task Resolve_InvalidQrCode_ReturnsNull()
    {
        var resolved = await _dbContext.Tables
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.QrCodeIdentifier == "nonexistent-qr");

        Assert.Null(resolved);
    }

    [Fact]
    public async Task Resolve_ClosedTable_HasClosedStatus()
    {
        var table = new Table
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            TableNumber = 3,
            Status = TableStatus.Closed,
            QrCodeIdentifier = "tc-table-3-closed",
            GameCenterId = _gameCenterId
        };
        _dbContext.Tables.Add(table);
        await _dbContext.SaveChangesAsync();

        var resolved = await _dbContext.Tables
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.QrCodeIdentifier == "tc-table-3-closed");

        Assert.NotNull(resolved);
        Assert.Equal(TableStatus.Closed, resolved.Status);
    }

    [Fact]
    public async Task Resolve_InactiveGameCenter_GameCenterIsInactive()
    {
        var inactiveGcId = Guid.NewGuid();
        _dbContext.GameCenters.Add(new GameCenter
        {
            Id = inactiveGcId,
            Name = "Inactive Center",
            IsActive = false,
            MaxTables = 5
        });
        var table = new Table
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            TableNumber = 1,
            Status = TableStatus.Active,
            QrCodeIdentifier = "inactive-gc-table",
            GameCenterId = inactiveGcId
        };
        _dbContext.Tables.Add(table);
        await _dbContext.SaveChangesAsync();

        var resolved = await _dbContext.Tables
            .IgnoreQueryFilters()
            .Include(t => t.GameCenter)
            .FirstOrDefaultAsync(t => t.QrCodeIdentifier == "inactive-gc-table");

        Assert.NotNull(resolved);
        Assert.False(resolved.GameCenter.IsActive);
    }

    [Fact]
    public async Task TenantFilter_FiltersTables()
    {
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();

        _tenantProvider.SetTenantId(null);
        _dbContext.Tables.AddRange(
            new Table { Id = Guid.NewGuid(), TenantId = tenant1, TableNumber = 1, QrCodeIdentifier = "t1-1", GameCenterId = _gameCenterId },
            new Table { Id = Guid.NewGuid(), TenantId = tenant2, TableNumber = 2, QrCodeIdentifier = "t2-1", GameCenterId = _gameCenterId },
            new Table { Id = Guid.NewGuid(), TenantId = tenant1, TableNumber = 3, QrCodeIdentifier = "t1-2", GameCenterId = _gameCenterId }
        );
        await _dbContext.SaveChangesAsync();

        _tenantProvider.SetTenantId(tenant1);
        var tables = await _dbContext.Tables.ToListAsync();

        Assert.Equal(2, tables.Count);
        Assert.All(tables, t => Assert.Equal(tenant1, t.TenantId));
    }

    [Fact]
    public async Task QrCodeIdentifier_IsUnique()
    {
        var table1 = new Table
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            TableNumber = 1,
            QrCodeIdentifier = "unique-qr",
            GameCenterId = _gameCenterId
        };
        _dbContext.Tables.Add(table1);
        await _dbContext.SaveChangesAsync();

        // Verify the unique index is configured
        var entityType = _dbContext.Model.FindEntityType(typeof(Table));
        var qrIndex = entityType?.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "QrCodeIdentifier"));

        Assert.NotNull(qrIndex);
        Assert.True(qrIndex.IsUnique);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
