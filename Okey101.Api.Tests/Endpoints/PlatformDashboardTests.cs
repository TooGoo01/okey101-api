using Microsoft.EntityFrameworkCore;
using Okey101.Api.Data;
using Okey101.Api.Models.Entities;
using Okey101.Api.Models.Enums;
using Okey101.Api.Services;

namespace Okey101.Api.Tests.Endpoints;

public class PlatformDashboardTests : IDisposable
{
    private readonly TenantProvider _tenantProvider;
    private readonly AppDbContext _dbContext;

    public PlatformDashboardTests()
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

    private async Task<GameSession> SeedSession(Guid tableId, Guid tenantId, GameSessionStatus status, DateTime createdAt)
    {
        var player = new Player
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Test Player",
            PhoneNumber = $"+1{Random.Shared.Next(1000000000, 1999999999)}",
            Role = UserRole.Player
        };
        _dbContext.Players.Add(player);

        var session = new GameSession
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TableId = tableId,
            GameName = "Test Game",
            Status = status,
            CreatedByPlayerId = player.Id,
            CreatedAt = createdAt
        };
        _dbContext.GameSessions.Add(session);
        await _dbContext.SaveChangesAsync();
        return session;
    }

    // --- InvokeX helper mirroring dashboard endpoint logic ---

    private async Task<(int gamesPlayedYesterday, int totalActiveCenters, int totalActiveTables, List<DashboardCenterResult> centers)> InvokeDashboard()
    {
        var todayStart = DateTime.UtcNow.Date;
        var yesterdayStart = todayStart.AddDays(-1);

        var validStatuses = new[]
        {
            GameSessionStatus.Approved,
            GameSessionStatus.Active,
            GameSessionStatus.Completed,
            GameSessionStatus.Closed
        };

        var gamesPlayedYesterday = await _dbContext.GameSessions
            .IgnoreQueryFilters()
            .CountAsync(s => s.CreatedAt >= yesterdayStart && s.CreatedAt < todayStart
                && validStatuses.Contains(s.Status));

        var centers = await _dbContext.GameCenters
            .OrderBy(gc => gc.Name)
            .ToListAsync();

        var centerIds = centers.Select(gc => gc.Id).ToList();

        var activeTableCounts = await _dbContext.Tables
            .IgnoreQueryFilters()
            .Where(t => centerIds.Contains(t.GameCenterId) && t.Status == TableStatus.Active)
            .GroupBy(t => t.GameCenterId)
            .Select(g => new { GameCenterId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GameCenterId, x => x.Count);

        var totalTableCounts = await _dbContext.Tables
            .IgnoreQueryFilters()
            .Where(t => centerIds.Contains(t.GameCenterId))
            .GroupBy(t => t.GameCenterId)
            .Select(g => new { GameCenterId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GameCenterId, x => x.Count);

        var tablesByCenter = await _dbContext.Tables
            .IgnoreQueryFilters()
            .Where(t => centerIds.Contains(t.GameCenterId))
            .Select(t => new { t.Id, t.GameCenterId })
            .ToListAsync();

        var tableToCenterMap = tablesByCenter.ToDictionary(t => t.Id, t => t.GameCenterId);
        var allTableIds = tablesByCenter.Select(t => t.Id).ToList();

        var activeSessions = await _dbContext.GameSessions
            .IgnoreQueryFilters()
            .Where(s => allTableIds.Contains(s.TableId) && s.Status == GameSessionStatus.Active)
            .Select(s => new { s.TableId })
            .ToListAsync();

        var gamesToday = await _dbContext.GameSessions
            .IgnoreQueryFilters()
            .Where(s => allTableIds.Contains(s.TableId)
                && s.CreatedAt >= todayStart
                && validStatuses.Contains(s.Status))
            .Select(s => new { s.TableId })
            .ToListAsync();

        var activeSessionsByCenter = new Dictionary<Guid, int>();
        var gamesTodayByCenter = new Dictionary<Guid, int>();

        foreach (var session in activeSessions)
        {
            var centerId = tableToCenterMap[session.TableId];
            activeSessionsByCenter[centerId] = activeSessionsByCenter.GetValueOrDefault(centerId) + 1;
        }

        foreach (var session in gamesToday)
        {
            var centerId = tableToCenterMap[session.TableId];
            gamesTodayByCenter[centerId] = gamesTodayByCenter.GetValueOrDefault(centerId) + 1;
        }

        var totalActiveCenters = centers.Count(c => c.IsActive);
        var totalActiveTables = activeTableCounts.Values.Sum();

        var centerResults = centers.Select(gc => new DashboardCenterResult
        {
            Id = gc.Id,
            Name = gc.Name,
            IsActive = gc.IsActive,
            ActiveTables = activeTableCounts.GetValueOrDefault(gc.Id, 0),
            TotalTables = totalTableCounts.GetValueOrDefault(gc.Id, 0),
            MaxTables = gc.MaxTables,
            ActiveSessionCount = activeSessionsByCenter.GetValueOrDefault(gc.Id, 0),
            GamesPlayedToday = gamesTodayByCenter.GetValueOrDefault(gc.Id, 0)
        }).ToList();

        return (gamesPlayedYesterday, totalActiveCenters, totalActiveTables, centerResults);
    }

    // --- Tests ---

    [Fact]
    public async Task Dashboard_ReturnsCorrectGamesPlayedYesterday()
    {
        var center = await SeedGameCenter("Center A");
        var table = await SeedTable(center.Id, 1);

        var yesterday = DateTime.UtcNow.Date.AddDays(-1).AddHours(10);
        await SeedSession(table.Id, center.Id, GameSessionStatus.Completed, yesterday);
        await SeedSession(table.Id, center.Id, GameSessionStatus.Approved, yesterday.AddHours(1));
        await SeedSession(table.Id, center.Id, GameSessionStatus.Active, yesterday.AddHours(2));

        var (gamesPlayedYesterday, _, _, _) = await InvokeDashboard();

        Assert.Equal(3, gamesPlayedYesterday);
    }

    [Fact]
    public async Task Dashboard_ReturnsZeroWhenNoGamesPlayedYesterday()
    {
        var center = await SeedGameCenter("Center A");
        var table = await SeedTable(center.Id, 1);

        // Only today's games
        var today = DateTime.UtcNow.Date.AddHours(5);
        await SeedSession(table.Id, center.Id, GameSessionStatus.Completed, today);

        var (gamesPlayedYesterday, _, _, _) = await InvokeDashboard();

        Assert.Equal(0, gamesPlayedYesterday);
    }

    [Fact]
    public async Task Dashboard_ReturnsCorrectPerCenterBreakdown()
    {
        var centerA = await SeedGameCenter("Center A", maxTables: 5);
        var centerB = await SeedGameCenter("Center B", maxTables: 10);

        var tableA1 = await SeedTable(centerA.Id, 1, TableStatus.Active);
        var tableA2 = await SeedTable(centerA.Id, 2, TableStatus.Active);
        var tableA3 = await SeedTable(centerA.Id, 3, TableStatus.Closed);

        var tableB1 = await SeedTable(centerB.Id, 1, TableStatus.Active);

        var today = DateTime.UtcNow.Date.AddHours(3);
        await SeedSession(tableA1.Id, centerA.Id, GameSessionStatus.Active, today);
        await SeedSession(tableA2.Id, centerA.Id, GameSessionStatus.Completed, today);
        await SeedSession(tableB1.Id, centerB.Id, GameSessionStatus.Active, today);

        var (_, totalActiveCenters, totalActiveTables, centers) = await InvokeDashboard();

        Assert.Equal(2, totalActiveCenters);
        Assert.Equal(3, totalActiveTables); // 2 active in A + 1 active in B

        var resultA = centers.First(c => c.Id == centerA.Id);
        Assert.Equal(2, resultA.ActiveTables);
        Assert.Equal(3, resultA.TotalTables);
        Assert.Equal(5, resultA.MaxTables);
        Assert.Equal(1, resultA.ActiveSessionCount); // Only Active status
        Assert.Equal(2, resultA.GamesPlayedToday); // Active + Completed

        var resultB = centers.First(c => c.Id == centerB.Id);
        Assert.Equal(1, resultB.ActiveTables);
        Assert.Equal(1, resultB.TotalTables);
        Assert.Equal(10, resultB.MaxTables);
        Assert.Equal(1, resultB.ActiveSessionCount);
        Assert.Equal(1, resultB.GamesPlayedToday);
    }

    [Fact]
    public async Task Dashboard_ExcludesPendingAndRejectedSessions()
    {
        var center = await SeedGameCenter("Center A");
        var table = await SeedTable(center.Id, 1);

        var yesterday = DateTime.UtcNow.Date.AddDays(-1).AddHours(10);
        var today = DateTime.UtcNow.Date.AddHours(3);

        // Valid statuses
        await SeedSession(table.Id, center.Id, GameSessionStatus.Approved, yesterday);
        await SeedSession(table.Id, center.Id, GameSessionStatus.Completed, today);

        // Invalid statuses — should not count
        await SeedSession(table.Id, center.Id, GameSessionStatus.Pending, yesterday);
        await SeedSession(table.Id, center.Id, GameSessionStatus.Rejected, yesterday);
        await SeedSession(table.Id, center.Id, GameSessionStatus.Pending, today);
        await SeedSession(table.Id, center.Id, GameSessionStatus.Rejected, today);

        var (gamesPlayedYesterday, _, _, centers) = await InvokeDashboard();

        Assert.Equal(1, gamesPlayedYesterday); // Only Approved from yesterday
        var result = centers.First(c => c.Id == center.Id);
        Assert.Equal(1, result.GamesPlayedToday); // Only Completed from today
    }

    [Fact]
    public async Task Dashboard_ReturnsAllCentersIncludingInactive()
    {
        await SeedGameCenter("Active Center", isActive: true);
        await SeedGameCenter("Inactive Center", isActive: false);

        var (_, totalActiveCenters, _, centers) = await InvokeDashboard();

        Assert.Equal(2, centers.Count);
        Assert.Equal(1, totalActiveCenters);
        Assert.Contains(centers, c => c.Name == "Active Center" && c.IsActive);
        Assert.Contains(centers, c => c.Name == "Inactive Center" && !c.IsActive);
    }

    [Fact]
    public async Task Dashboard_WithNoCentersReturnsEmptyArrayAndZeroCounts()
    {
        var (gamesPlayedYesterday, totalActiveCenters, totalActiveTables, centers) = await InvokeDashboard();

        Assert.Equal(0, gamesPlayedYesterday);
        Assert.Equal(0, totalActiveCenters);
        Assert.Equal(0, totalActiveTables);
        Assert.Empty(centers);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}

public class DashboardCenterResult
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int ActiveTables { get; set; }
    public int TotalTables { get; set; }
    public int MaxTables { get; set; }
    public int ActiveSessionCount { get; set; }
    public int GamesPlayedToday { get; set; }
}
