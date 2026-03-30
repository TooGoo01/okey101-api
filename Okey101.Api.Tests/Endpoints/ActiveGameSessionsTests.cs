using Microsoft.EntityFrameworkCore;
using Okey101.Api.Data;
using Okey101.Api.Models.Entities;
using Okey101.Api.Models.Enums;
using Okey101.Api.Models.Responses;
using Okey101.Api.Services;

namespace Okey101.Api.Tests.Endpoints;

public class ActiveGameSessionsTests : IDisposable
{
    private readonly TenantProvider _tenantProvider;
    private readonly AppDbContext _dbContext;
    private readonly Guid _gameCenterId;
    private readonly Guid _tenantId;
    private readonly Guid _tableId;
    private readonly Guid _playerId;

    public ActiveGameSessionsTests()
    {
        _tenantProvider = new TenantProvider();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new AppDbContext(options, _tenantProvider);
        _gameCenterId = Guid.NewGuid();
        _tenantId = Guid.NewGuid();
        _tableId = Guid.NewGuid();
        _playerId = Guid.NewGuid();

        // Seed base data with null tenant (superadmin mode) to bypass filters
        _tenantProvider.SetTenantId(null);
        _dbContext.GameCenters.Add(new GameCenter
        {
            Id = _gameCenterId,
            Name = "Test Game Center",
            IsActive = true,
            MaxTables = 10
        });
        _dbContext.Tables.Add(new Table
        {
            Id = _tableId,
            TenantId = _tenantId,
            TableNumber = 5,
            Status = TableStatus.Active,
            QrCodeIdentifier = "active-test-qr",
            GameCenterId = _gameCenterId
        });
        _dbContext.Players.Add(new Player
        {
            Id = _playerId,
            Name = "Alice",
            PhoneNumber = "encrypted",
            PhoneNumberHash = "hash-active",
            TenantId = _tenantId
        });
        _dbContext.SaveChanges();
    }

    private GameSession CreateSession(GameSessionStatus status, string name = "Test Game")
    {
        var session = new GameSession
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            TableId = _tableId,
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
    /// Simulates what the GET /active endpoint does: queries Active + Approved sessions
    /// with tenant filtering, computes current round from ScoreEntries.
    /// </summary>
    private async Task<List<AdminGameSessionResponse>> InvokeGetActiveSessions()
    {
        var sessions = await _dbContext.GameSessions
            .Include(gs => gs.Table)
                .ThenInclude(t => t.GameCenter)
            .Include(gs => gs.Teams)
                .ThenInclude(t => t.Players)
            .Where(gs => gs.Status == GameSessionStatus.Active || gs.Status == GameSessionStatus.Approved)
            .OrderByDescending(gs => gs.CreatedAt)
            .ToListAsync();

        var sessionIds = sessions.Select(s => s.Id).ToList();
        var currentRounds = sessionIds.Count > 0
            ? await _dbContext.ScoreEntries
                .Where(e => sessionIds.Contains(e.GameSessionId) && !e.IsRemoved)
                .GroupBy(e => e.GameSessionId)
                .Select(g => new { SessionId = g.Key, MaxRound = g.Max(e => e.RoundNumber) })
                .ToDictionaryAsync(x => x.SessionId, x => x.MaxRound)
            : new Dictionary<Guid, int>();

        var allPlayerIds = sessions
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

        return sessions.Select(gs =>
        {
            var currentRound = currentRounds.TryGetValue(gs.Id, out var maxRound)
                ? maxRound
                : 1;

            return new AdminGameSessionResponse
            {
                SessionId = gs.Id,
                GameName = gs.GameName,
                Status = gs.Status.ToString(),
                CreatedAt = gs.CreatedAt,
                ApprovedAt = gs.ApprovedAt,
                TableNumber = gs.Table.TableNumber,
                GameCenterName = gs.Table.GameCenter.Name,
                CurrentRound = currentRound,
                Teams = gs.Teams.Select(t => new GameTeamResponse
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
        }).ToList();
    }

    [Fact]
    public async Task ActiveSessions_ReturnsOnlyActiveAndApproved()
    {
        // Seed sessions with various statuses
        _tenantProvider.SetTenantId(null);
        _dbContext.GameSessions.Add(CreateSession(GameSessionStatus.Active, "Active Game"));
        _dbContext.GameSessions.Add(CreateSession(GameSessionStatus.Approved, "Approved Game"));
        _dbContext.GameSessions.Add(CreateSession(GameSessionStatus.Pending, "Pending Game"));
        _dbContext.GameSessions.Add(CreateSession(GameSessionStatus.Completed, "Completed Game"));
        _dbContext.GameSessions.Add(CreateSession(GameSessionStatus.Rejected, "Rejected Game"));
        _dbContext.GameSessions.Add(CreateSession(GameSessionStatus.Closed, "Closed Game"));
        await _dbContext.SaveChangesAsync();

        // Query as tenant admin
        _tenantProvider.SetTenantId(_tenantId);
        var results = await InvokeGetActiveSessions();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.GameName == "Active Game" && r.Status == "Active");
        Assert.Contains(results, r => r.GameName == "Approved Game" && r.Status == "Approved");
    }

    [Fact]
    public async Task ActiveSessions_EmptyWhenNoActiveSessions()
    {
        // Seed only completed and pending sessions
        _tenantProvider.SetTenantId(null);
        _dbContext.GameSessions.Add(CreateSession(GameSessionStatus.Pending, "Pending"));
        _dbContext.GameSessions.Add(CreateSession(GameSessionStatus.Completed, "Done"));
        await _dbContext.SaveChangesAsync();

        _tenantProvider.SetTenantId(_tenantId);
        var results = await InvokeGetActiveSessions();

        Assert.Empty(results);
    }

    [Fact]
    public async Task ActiveSessions_IncludesTeamAndTableInfo()
    {
        _tenantProvider.SetTenantId(null);
        _dbContext.GameSessions.Add(CreateSession(GameSessionStatus.Active, "Info Test"));
        await _dbContext.SaveChangesAsync();

        _tenantProvider.SetTenantId(_tenantId);
        var results = await InvokeGetActiveSessions();

        Assert.Single(results);
        var session = results[0];
        Assert.Equal(5, session.TableNumber);
        Assert.Equal("Test Game Center", session.GameCenterName);
        Assert.Equal(2, session.Teams.Count);

        var team1 = session.Teams.First(t => t.TeamNumber == 1);
        Assert.Equal("Team Alpha", team1.TeamName);
        Assert.Single(team1.Players);
        Assert.Equal("Alice", team1.Players[0].Name);
        Assert.False(team1.Players[0].IsGuest);

        var team2 = session.Teams.First(t => t.TeamNumber == 2);
        Assert.Equal("Team Beta", team2.TeamName);
        Assert.Single(team2.Players);
        Assert.Equal("Guest Bob", team2.Players[0].Name);
        Assert.True(team2.Players[0].IsGuest);
    }

    [Fact]
    public async Task ActiveSessions_CurrentRoundFromScoreEntries()
    {
        _tenantProvider.SetTenantId(null);
        var session = CreateSession(GameSessionStatus.Active, "Round Test");
        _dbContext.GameSessions.Add(session);

        // Add score entries up to round 4
        _dbContext.ScoreEntries.Add(new ScoreEntry
        {
            Id = Guid.NewGuid(),
            GameSessionId = session.Id,
            TeamNumber = 1,
            RoundNumber = 1,
            ScoreType = ScoreType.EndOfRound,
            Value = 50,
            CreatedByPlayerId = _playerId
        });
        _dbContext.ScoreEntries.Add(new ScoreEntry
        {
            Id = Guid.NewGuid(),
            GameSessionId = session.Id,
            TeamNumber = 1,
            RoundNumber = 4,
            ScoreType = ScoreType.EndOfRound,
            Value = 30,
            CreatedByPlayerId = _playerId
        });
        await _dbContext.SaveChangesAsync();

        _tenantProvider.SetTenantId(_tenantId);
        var results = await InvokeGetActiveSessions();

        Assert.Single(results);
        Assert.Equal(4, results[0].CurrentRound);
    }

    [Fact]
    public async Task ActiveSessions_DefaultsToRound1WhenNoScoreEntries()
    {
        _tenantProvider.SetTenantId(null);
        _dbContext.GameSessions.Add(CreateSession(GameSessionStatus.Approved, "New Approved"));
        await _dbContext.SaveChangesAsync();

        _tenantProvider.SetTenantId(_tenantId);
        var results = await InvokeGetActiveSessions();

        Assert.Single(results);
        Assert.Equal(1, results[0].CurrentRound);
    }

    [Fact]
    public async Task ActiveSessions_TenantIsolation_OnlyReturnsTenantSessions()
    {
        var otherTenantId = Guid.NewGuid();
        var otherGameCenterId = Guid.NewGuid();
        var otherTableId = Guid.NewGuid();

        _tenantProvider.SetTenantId(null);

        // Other tenant's game center and table
        _dbContext.GameCenters.Add(new GameCenter
        {
            Id = otherGameCenterId,
            Name = "Other Center",
            IsActive = true,
            MaxTables = 5
        });
        _dbContext.Tables.Add(new Table
        {
            Id = otherTableId,
            TenantId = otherTenantId,
            TableNumber = 1,
            Status = TableStatus.Active,
            QrCodeIdentifier = "other-qr",
            GameCenterId = otherGameCenterId
        });

        // Our tenant's session
        _dbContext.GameSessions.Add(CreateSession(GameSessionStatus.Active, "Our Game"));

        // Other tenant's session
        var otherSession = new GameSession
        {
            Id = Guid.NewGuid(),
            TenantId = otherTenantId,
            TableId = otherTableId,
            GameName = "Their Game",
            Status = GameSessionStatus.Active,
            CreatedByPlayerId = _playerId,
            CreatedAt = DateTime.UtcNow
        };
        otherSession.Teams.Add(new GameTeam
        {
            Id = Guid.NewGuid(),
            GameSessionId = otherSession.Id,
            TeamName = "T1",
            TeamNumber = 1
        });
        otherSession.Teams.Add(new GameTeam
        {
            Id = Guid.NewGuid(),
            GameSessionId = otherSession.Id,
            TeamName = "T2",
            TeamNumber = 2
        });
        _dbContext.GameSessions.Add(otherSession);
        await _dbContext.SaveChangesAsync();

        // Query as our tenant
        _tenantProvider.SetTenantId(_tenantId);
        var results = await InvokeGetActiveSessions();

        Assert.Single(results);
        Assert.Equal("Our Game", results[0].GameName);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
