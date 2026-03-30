using Microsoft.EntityFrameworkCore;
using Okey101.Api.Data;
using Okey101.Api.Models.Entities;
using Okey101.Api.Models.Enums;
using Okey101.Api.Models.Responses;
using Okey101.Api.Services;

namespace Okey101.Api.Tests.Endpoints;

public class TableStatusTests : IDisposable
{
    private readonly TenantProvider _tenantProvider;
    private readonly AppDbContext _dbContext;
    private readonly Guid _gameCenterId;
    private readonly Guid _tenantId;
    private readonly Guid _playerId;

    public TableStatusTests()
    {
        _tenantProvider = new TenantProvider();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new AppDbContext(options, _tenantProvider);
        _gameCenterId = Guid.NewGuid();
        _tenantId = Guid.NewGuid();
        _playerId = Guid.NewGuid();

        // Seed base data with null tenant to bypass filters
        _tenantProvider.SetTenantId(null);
        _dbContext.GameCenters.Add(new GameCenter
        {
            Id = _gameCenterId,
            Name = "Test Game Center",
            IsActive = true,
            MaxTables = 10
        });
        _dbContext.Players.Add(new Player
        {
            Id = _playerId,
            Name = "Alice",
            PhoneNumber = "encrypted",
            PhoneNumberHash = "hash-table-test",
            TenantId = _tenantId
        });
        _dbContext.SaveChanges();
    }

    private Guid AddTable(int tableNumber, TableStatus status = TableStatus.Active)
    {
        var tableId = Guid.NewGuid();
        _dbContext.Tables.Add(new Table
        {
            Id = tableId,
            TenantId = _tenantId,
            TableNumber = tableNumber,
            Status = status,
            QrCodeIdentifier = $"qr-{tableNumber}-{tableId}",
            GameCenterId = _gameCenterId
        });
        return tableId;
    }

    private GameSession CreateSessionForTable(Guid tableId, GameSessionStatus status, string name = "Test Game")
    {
        var session = new GameSession
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            TableId = tableId,
            GameName = name,
            Status = status,
            CreatedByPlayerId = _playerId,
            CreatedAt = DateTime.UtcNow
        };

        var team1 = new GameTeam
        {
            Id = Guid.NewGuid(),
            GameSessionId = session.Id,
            TeamName = "Team Alpha",
            TeamNumber = 1
        };
        team1.Players.Add(new GamePlayer
        {
            Id = Guid.NewGuid(),
            GameTeamId = team1.Id,
            PlayerId = _playerId,
            IsGuest = false
        });

        var team2 = new GameTeam
        {
            Id = Guid.NewGuid(),
            GameSessionId = session.Id,
            TeamName = "Team Beta",
            TeamNumber = 2
        };
        team2.Players.Add(new GamePlayer
        {
            Id = Guid.NewGuid(),
            GameTeamId = team2.Id,
            GuestName = "Guest Bob",
            IsGuest = true
        });

        session.Teams.Add(team1);
        session.Teams.Add(team2);
        return session;
    }

    /// <summary>
    /// Simulates what the GET /api/v1/tables endpoint does: queries all active tables
    /// with derived display status from current game sessions.
    /// </summary>
    private async Task<List<AdminTableResponse>> InvokeGetTables()
    {
        var tables = await _dbContext.Tables
            .Where(t => t.Status == TableStatus.Active)
            .OrderBy(t => t.TableNumber)
            .ToListAsync();

        var tableIds = tables.Select(t => t.Id).ToList();

        var activeSessions = tableIds.Count > 0
            ? await _dbContext.GameSessions
                .Include(gs => gs.Teams)
                    .ThenInclude(t => t.Players)
                .Where(gs => tableIds.Contains(gs.TableId)
                    && (gs.Status == GameSessionStatus.Active
                        || gs.Status == GameSessionStatus.Approved
                        || gs.Status == GameSessionStatus.Pending))
                .ToListAsync()
            : new List<GameSession>();

        var sessionByTable = activeSessions
            .GroupBy(gs => gs.TableId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(gs => gs.Status switch
                {
                    GameSessionStatus.Active => 0,
                    GameSessionStatus.Approved => 1,
                    GameSessionStatus.Pending => 2,
                    _ => 3
                }).First());

        var allPlayerIds = activeSessions
            .SelectMany(gs => gs.Teams)
            .SelectMany(t => t.Players)
            .Where(p => !p.IsGuest && p.PlayerId.HasValue)
            .Select(p => p.PlayerId!.Value)
            .Distinct()
            .ToList();

        var playerNameMap = allPlayerIds.Count > 0
            ? await _dbContext.Players
                .Where(p => allPlayerIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name)
            : new Dictionary<Guid, string>();

        return tables.Select(table =>
        {
            var hasSession = sessionByTable.TryGetValue(table.Id, out var session);

            var displayStatus = hasSession
                ? session!.Status switch
                {
                    GameSessionStatus.Active => "Active",
                    GameSessionStatus.Approved => "Approved",
                    GameSessionStatus.Pending => "Pending",
                    _ => "Empty"
                }
                : "Empty";

            TableSessionInfo? sessionInfo = null;
            if (hasSession && session is not null)
            {
                sessionInfo = new TableSessionInfo
                {
                    SessionId = session.Id,
                    GameName = session.GameName,
                    Status = session.Status.ToString(),
                    CreatedAt = session.CreatedAt,
                    Teams = session.Teams.Select(t => new GameTeamResponse
                    {
                        TeamId = t.Id,
                        TeamName = t.TeamName,
                        TeamNumber = t.TeamNumber,
                        Players = t.Players.Select(p => new GamePlayerResponse
                        {
                            PlayerId = p.IsGuest ? p.Id : p.PlayerId!.Value,
                            Name = p.IsGuest
                                ? p.GuestName!
                                : playerNameMap.GetValueOrDefault(p.PlayerId!.Value, "Unknown"),
                            IsGuest = p.IsGuest
                        }).ToList()
                    }).ToList()
                };
            }

            return new AdminTableResponse
            {
                TableId = table.Id,
                TableNumber = table.TableNumber,
                DisplayStatus = displayStatus,
                CurrentSession = sessionInfo
            };
        }).ToList();
    }

    [Fact]
    public async Task Tables_ReturnsAllTablesWithCorrectDisplayStatus()
    {
        _tenantProvider.SetTenantId(null);
        var table1Id = AddTable(1);
        var table2Id = AddTable(2);
        var table3Id = AddTable(3);
        var table4Id = AddTable(4);

        _dbContext.GameSessions.Add(CreateSessionForTable(table1Id, GameSessionStatus.Active, "Active Game"));
        _dbContext.GameSessions.Add(CreateSessionForTable(table2Id, GameSessionStatus.Pending, "Pending Game"));
        _dbContext.GameSessions.Add(CreateSessionForTable(table3Id, GameSessionStatus.Approved, "Approved Game"));
        // table4 has no session
        await _dbContext.SaveChangesAsync();

        _tenantProvider.SetTenantId(_tenantId);
        var results = await InvokeGetTables();

        Assert.Equal(4, results.Count);
        Assert.Equal("Active", results.First(r => r.TableNumber == 1).DisplayStatus);
        Assert.Equal("Pending", results.First(r => r.TableNumber == 2).DisplayStatus);
        Assert.Equal("Approved", results.First(r => r.TableNumber == 3).DisplayStatus);
        Assert.Equal("Empty", results.First(r => r.TableNumber == 4).DisplayStatus);
    }

    [Fact]
    public async Task Tables_ActiveSessionShowsGameInfo()
    {
        _tenantProvider.SetTenantId(null);
        var tableId = AddTable(5);
        _dbContext.GameSessions.Add(CreateSessionForTable(tableId, GameSessionStatus.Active, "My Active Game"));
        await _dbContext.SaveChangesAsync();

        _tenantProvider.SetTenantId(_tenantId);
        var results = await InvokeGetTables();

        Assert.Single(results);
        var table = results[0];
        Assert.Equal("Active", table.DisplayStatus);
        Assert.NotNull(table.CurrentSession);
        Assert.Equal("My Active Game", table.CurrentSession!.GameName);
        Assert.Equal(2, table.CurrentSession.Teams.Count);

        var team1 = table.CurrentSession.Teams.First(t => t.TeamNumber == 1);
        Assert.Equal("Team Alpha", team1.TeamName);
        Assert.Equal("Alice", team1.Players[0].Name);

        var team2 = table.CurrentSession.Teams.First(t => t.TeamNumber == 2);
        Assert.Equal("Team Beta", team2.TeamName);
        Assert.Equal("Guest Bob", team2.Players[0].Name);
    }

    [Fact]
    public async Task Tables_PendingSessionShowsPendingStatus()
    {
        _tenantProvider.SetTenantId(null);
        var tableId = AddTable(3);
        _dbContext.GameSessions.Add(CreateSessionForTable(tableId, GameSessionStatus.Pending, "Awaiting Game"));
        await _dbContext.SaveChangesAsync();

        _tenantProvider.SetTenantId(_tenantId);
        var results = await InvokeGetTables();

        Assert.Single(results);
        Assert.Equal("Pending", results[0].DisplayStatus);
        Assert.NotNull(results[0].CurrentSession);
        Assert.Equal("Awaiting Game", results[0].CurrentSession!.GameName);
    }

    [Fact]
    public async Task Tables_EmptyTableShowsEmptyStatus()
    {
        _tenantProvider.SetTenantId(null);
        AddTable(7);
        // Also add a completed session — should NOT affect display status
        var tableId = _dbContext.Tables.Local.First().Id;
        _dbContext.GameSessions.Add(CreateSessionForTable(tableId, GameSessionStatus.Completed, "Old Game"));
        await _dbContext.SaveChangesAsync();

        _tenantProvider.SetTenantId(_tenantId);
        var results = await InvokeGetTables();

        Assert.Single(results);
        Assert.Equal("Empty", results[0].DisplayStatus);
        Assert.Null(results[0].CurrentSession);
    }

    [Fact]
    public async Task Tables_TenantIsolation_OnlyReturnsTenantTables()
    {
        var otherTenantId = Guid.NewGuid();
        var otherGameCenterId = Guid.NewGuid();

        _tenantProvider.SetTenantId(null);

        // Other tenant's game center
        _dbContext.GameCenters.Add(new GameCenter
        {
            Id = otherGameCenterId,
            Name = "Other Center",
            IsActive = true,
            MaxTables = 5
        });

        // Our tenant's table
        AddTable(1);

        // Other tenant's table
        _dbContext.Tables.Add(new Table
        {
            Id = Guid.NewGuid(),
            TenantId = otherTenantId,
            TableNumber = 2,
            Status = TableStatus.Active,
            QrCodeIdentifier = "other-qr-iso",
            GameCenterId = otherGameCenterId
        });
        await _dbContext.SaveChangesAsync();

        // Query as our tenant
        _tenantProvider.SetTenantId(_tenantId);
        var results = await InvokeGetTables();

        Assert.Single(results);
        Assert.Equal(1, results[0].TableNumber);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
