using Microsoft.EntityFrameworkCore;
using Okey101.Api.Data;
using Okey101.Api.Models.Entities;
using Okey101.Api.Models.Enums;
using Okey101.Api.Services;

namespace Okey101.Api.Tests.Endpoints;

public class PlatformTableSlotTests : IDisposable
{
    private readonly TenantProvider _tenantProvider;
    private readonly AppDbContext _dbContext;

    public PlatformTableSlotTests()
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

    private async Task<Table> SeedTable(Guid gameCenterId, int tableNumber, TableStatus status = TableStatus.Active)
    {
        var table = new Table
        {
            Id = Guid.NewGuid(),
            TenantId = gameCenterId,
            TableNumber = tableNumber,
            Status = status,
            QrCodeIdentifier = $"gc-{gameCenterId}-t-{tableNumber}-{Guid.NewGuid().ToString("N")[..8]}",
            GameCenterId = gameCenterId
        };
        _dbContext.Tables.Add(table);
        await _dbContext.SaveChangesAsync();
        return table;
    }

    // --- InvokeX helpers mirroring endpoint logic ---

    private async Task<(GameCenter? center, string? errorCode)> InvokeUpdateTableSlots(Guid id, int maxTables)
    {
        if (maxTables < 1 || maxTables > 200)
            return (null, "VALIDATION_ERROR");

        var center = await _dbContext.GameCenters.FirstOrDefaultAsync(gc => gc.Id == id);
        if (center is null)
            return (null, "GAME_CENTER_NOT_FOUND");

        center.MaxTables = maxTables;
        center.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return (center, null);
    }

    private async Task<(Table? table, bool created, string? errorCode)> InvokeCreateTable(Guid gameCenterId)
    {
        var center = await _dbContext.GameCenters.FirstOrDefaultAsync(gc => gc.Id == gameCenterId);
        if (center is null)
            return (null, false, "GAME_CENTER_NOT_FOUND");

        if (!center.IsActive)
            return (null, false, "GAME_CENTER_INACTIVE");

        var activeTableCount = await _dbContext.Tables
            .IgnoreQueryFilters()
            .CountAsync(t => t.GameCenterId == gameCenterId && t.Status == TableStatus.Active);

        if (activeTableCount >= center.MaxTables)
            return (null, false, "TABLE_LIMIT_REACHED");

        var maxTableNumber = await _dbContext.Tables
            .IgnoreQueryFilters()
            .Where(t => t.GameCenterId == gameCenterId)
            .MaxAsync(t => (int?)t.TableNumber) ?? 0;

        var tableNumber = maxTableNumber + 1;
        var shortGuid = Guid.NewGuid().ToString("N")[..8];
        var qrCode = $"gc-{gameCenterId}-t-{tableNumber}-{shortGuid}";

        var table = new Table
        {
            Id = Guid.NewGuid(),
            TenantId = center.Id,
            TableNumber = tableNumber,
            Status = TableStatus.Active,
            QrCodeIdentifier = qrCode,
            GameCenterId = center.Id
        };

        _dbContext.Tables.Add(table);
        await _dbContext.SaveChangesAsync();

        return (table, true, null);
    }

    private async Task<(List<Table>? tables, int activeCount, int closedCount, int maxTables, string? errorCode)> InvokeListTables(Guid gameCenterId)
    {
        var center = await _dbContext.GameCenters.FirstOrDefaultAsync(gc => gc.Id == gameCenterId);
        if (center is null)
            return (null, 0, 0, 0, "GAME_CENTER_NOT_FOUND");

        var tables = await _dbContext.Tables
            .IgnoreQueryFilters()
            .Where(t => t.GameCenterId == gameCenterId)
            .OrderBy(t => t.TableNumber)
            .ToListAsync();

        var activeCount = tables.Count(t => t.Status == TableStatus.Active);
        var closedCount = tables.Count(t => t.Status == TableStatus.Closed);

        return (tables, activeCount, closedCount, center.MaxTables, null);
    }

    // --- Tests ---

    [Fact]
    public async Task UpdateTableSlots_ChangesMaxTables()
    {
        var center = await SeedGameCenter("Test Center", maxTables: 10);

        var (updated, errorCode) = await InvokeUpdateTableSlots(center.Id, 15);

        Assert.Null(errorCode);
        Assert.NotNull(updated);
        Assert.Equal(15, updated!.MaxTables);
        Assert.NotNull(updated.UpdatedAt);
    }

    [Fact]
    public async Task UpdateTableSlots_InvalidValueReturns400()
    {
        var center = await SeedGameCenter("Test Center");

        var (_, errorCode1) = await InvokeUpdateTableSlots(center.Id, 0);
        Assert.Equal("VALIDATION_ERROR", errorCode1);

        var (_, errorCode2) = await InvokeUpdateTableSlots(center.Id, 201);
        Assert.Equal("VALIDATION_ERROR", errorCode2);
    }

    [Fact]
    public async Task CreateTable_SucceedsWhenUnderLimit()
    {
        var center = await SeedGameCenter("Test Center", maxTables: 5);
        await SeedTable(center.Id, 1);
        await SeedTable(center.Id, 2);

        var (table, created, errorCode) = await InvokeCreateTable(center.Id);

        Assert.True(created);
        Assert.Null(errorCode);
        Assert.NotNull(table);
        Assert.Equal(3, table!.TableNumber);
        Assert.Equal(TableStatus.Active, table.Status);
        Assert.Equal(center.Id, table.TenantId);
        Assert.StartsWith($"gc-{center.Id}-t-3-", table.QrCodeIdentifier);
    }

    [Fact]
    public async Task CreateTable_RejectedWhenAtLimit()
    {
        var center = await SeedGameCenter("Test Center", maxTables: 2);
        await SeedTable(center.Id, 1);
        await SeedTable(center.Id, 2);

        var (_, created, errorCode) = await InvokeCreateTable(center.Id);

        Assert.False(created);
        Assert.Equal("TABLE_LIMIT_REACHED", errorCode);
    }

    [Fact]
    public async Task CreateTable_InactiveCenterReturns409()
    {
        var center = await SeedGameCenter("Inactive Center", isActive: false, maxTables: 10);

        var (_, created, errorCode) = await InvokeCreateTable(center.Id);

        Assert.False(created);
        Assert.Equal("GAME_CENTER_INACTIVE", errorCode);
    }

    [Fact]
    public async Task CreateTable_AutoAssignsSequentialTableNumber()
    {
        var center = await SeedGameCenter("Test Center", maxTables: 10);

        var (table1, _, _) = await InvokeCreateTable(center.Id);
        var (table2, _, _) = await InvokeCreateTable(center.Id);
        var (table3, _, _) = await InvokeCreateTable(center.Id);

        Assert.Equal(1, table1!.TableNumber);
        Assert.Equal(2, table2!.TableNumber);
        Assert.Equal(3, table3!.TableNumber);
    }

    [Fact]
    public async Task ReduceTableSlots_BelowActiveCountIsAllowed()
    {
        var center = await SeedGameCenter("Test Center", maxTables: 10);
        await SeedTable(center.Id, 1);
        await SeedTable(center.Id, 2);
        await SeedTable(center.Id, 3);

        // Reduce to 1 even though 3 active tables exist
        var (updated, errorCode) = await InvokeUpdateTableSlots(center.Id, 1);

        Assert.Null(errorCode);
        Assert.NotNull(updated);
        Assert.Equal(1, updated!.MaxTables);
    }

    [Fact]
    public async Task ListTables_ReturnsAllTablesWithCorrectCounts()
    {
        var center = await SeedGameCenter("Test Center", maxTables: 10);
        await SeedTable(center.Id, 1, TableStatus.Active);
        await SeedTable(center.Id, 2, TableStatus.Active);
        await SeedTable(center.Id, 3, TableStatus.Closed);

        var (tables, activeCount, closedCount, maxTables, errorCode) = await InvokeListTables(center.Id);

        Assert.Null(errorCode);
        Assert.NotNull(tables);
        Assert.Equal(3, tables!.Count);
        Assert.Equal(2, activeCount);
        Assert.Equal(1, closedCount);
        Assert.Equal(10, maxTables);
        // Verify ordering by table number
        Assert.Equal(1, tables[0].TableNumber);
        Assert.Equal(2, tables[1].TableNumber);
        Assert.Equal(3, tables[2].TableNumber);
    }

    [Fact]
    public async Task CreateTable_AfterReducingSlots_IsBlockedIfOverLimit()
    {
        var center = await SeedGameCenter("Test Center", maxTables: 5);
        await SeedTable(center.Id, 1);
        await SeedTable(center.Id, 2);
        await SeedTable(center.Id, 3);

        // Reduce slots to 2 (below the 3 active tables)
        await InvokeUpdateTableSlots(center.Id, 2);

        // Try to create another table — should be blocked
        var (_, created, errorCode) = await InvokeCreateTable(center.Id);

        Assert.False(created);
        Assert.Equal("TABLE_LIMIT_REACHED", errorCode);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
