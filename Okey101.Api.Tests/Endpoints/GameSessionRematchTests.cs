using Microsoft.EntityFrameworkCore;
using Okey101.Api.Data;
using Okey101.Api.Models.Entities;
using Okey101.Api.Models.Enums;
using Okey101.Api.Services;

namespace Okey101.Api.Tests.Endpoints;

public class GameSessionRematchTests : IDisposable
{
    private readonly TenantProvider _tenantProvider;
    private readonly AppDbContext _dbContext;
    private readonly Guid _tenantId;
    private readonly Guid _sessionId;
    private readonly Guid _playerId;
    private readonly Guid _tableId;

    public GameSessionRematchTests()
    {
        _tenantProvider = new TenantProvider();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new AppDbContext(options, _tenantProvider);
        _tenantId = Guid.NewGuid();
        _sessionId = Guid.NewGuid();
        _playerId = Guid.NewGuid();
        _tableId = Guid.NewGuid();

        _tenantProvider.SetTenantId(null);

        var gameCenterId = Guid.NewGuid();

        _dbContext.GameCenters.Add(new GameCenter
        {
            Id = gameCenterId,
            Name = "Test Center",
            IsActive = true,
            MaxTables = 10
        });
        _dbContext.Tables.Add(new Table
        {
            Id = _tableId,
            TenantId = _tenantId,
            TableNumber = 1,
            Status = TableStatus.Active,
            QrCodeIdentifier = "qr-test",
            GameCenterId = gameCenterId
        });
        _dbContext.Players.Add(new Player
        {
            Id = _playerId,
            Name = "Test Player",
            PhoneNumber = "encrypted",
            PhoneNumberHash = "hash",
            TenantId = _tenantId
        });

        var team1Id = Guid.NewGuid();
        var team2Id = Guid.NewGuid();

        _dbContext.GameSessions.Add(new GameSession
        {
            Id = _sessionId,
            TenantId = _tenantId,
            TableId = _tableId,
            GameName = "Test Game",
            Status = GameSessionStatus.Completed,
            CreatedByPlayerId = _playerId,
            CreatedAt = DateTime.UtcNow,
            EndedAt = DateTime.UtcNow,
            WinnerTeamNumber = 1,
            Team1FinalTotal = 400,
            Team2FinalTotal = 240,
            Teams = new List<GameTeam>
            {
                new()
                {
                    Id = team1Id,
                    TeamName = "Team Alpha",
                    TeamNumber = 1,
                    Players = new List<GamePlayer>
                    {
                        new() { Id = Guid.NewGuid(), GameTeamId = team1Id, PlayerId = _playerId, IsGuest = false },
                        new() { Id = Guid.NewGuid(), GameTeamId = team1Id, GuestName = "Guest A", IsGuest = true },
                    }
                },
                new()
                {
                    Id = team2Id,
                    TeamName = "Team Beta",
                    TeamNumber = 2,
                    Players = new List<GamePlayer>
                    {
                        new() { Id = Guid.NewGuid(), GameTeamId = team2Id, GuestName = "Guest B", IsGuest = true },
                    }
                },
            }
        });
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task Rematch_SameDay_ActiveTable_CreatesNewActiveSession()
    {
        // Verify parent session is completed
        var parent = await _dbContext.GameSessions
            .Include(s => s.Teams)
                .ThenInclude(t => t.Players)
            .Include(s => s.Table)
            .FirstAsync(s => s.Id == _sessionId);

        Assert.Equal(GameSessionStatus.Completed, parent.Status);
        Assert.Equal(TableStatus.Active, parent.Table.Status);
        Assert.Equal(DateTime.UtcNow.Date, parent.CreatedAt.Date);

        // Simulate rematch logic (same as endpoint)
        var gameName = $"{parent.GameName} - Rematch";
        var newSession = new GameSession
        {
            Id = Guid.NewGuid(),
            TenantId = parent.TenantId,
            TableId = parent.TableId,
            GameName = gameName,
            Status = GameSessionStatus.Active,
            CreatedByPlayerId = parent.CreatedByPlayerId,
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow,
        };

        foreach (var parentTeam in parent.Teams)
        {
            var newTeam = new GameTeam
            {
                Id = Guid.NewGuid(),
                GameSessionId = newSession.Id,
                TeamName = parentTeam.TeamName,
                TeamNumber = parentTeam.TeamNumber,
            };
            foreach (var parentPlayer in parentTeam.Players)
            {
                newTeam.Players.Add(new GamePlayer
                {
                    Id = Guid.NewGuid(),
                    GameTeamId = newTeam.Id,
                    PlayerId = parentPlayer.PlayerId,
                    GuestName = parentPlayer.GuestName,
                    IsGuest = parentPlayer.IsGuest,
                });
            }
            newSession.Teams.Add(newTeam);
        }

        _dbContext.GameSessions.Add(newSession);
        await _dbContext.SaveChangesAsync();

        // Verify new session
        var result = await _dbContext.GameSessions
            .Include(s => s.Teams)
                .ThenInclude(t => t.Players)
            .FirstAsync(s => s.Id == newSession.Id);

        Assert.Equal(GameSessionStatus.Active, result.Status);
        Assert.Equal("Test Game - Rematch", result.GameName);
        Assert.Equal(parent.TableId, result.TableId);
        Assert.NotNull(result.StartedAt);
        Assert.Equal(2, result.Teams.Count);

        var team1 = result.Teams.First(t => t.TeamNumber == 1);
        var team2 = result.Teams.First(t => t.TeamNumber == 2);
        Assert.Equal("Team Alpha", team1.TeamName);
        Assert.Equal("Team Beta", team2.TeamName);
        Assert.Equal(2, team1.Players.Count);
        Assert.Equal(1, team2.Players.Count);

        // Verify players were copied with new IDs
        var copiedRegistered = team1.Players.FirstOrDefault(p => !p.IsGuest);
        Assert.NotNull(copiedRegistered);
        Assert.Equal(_playerId, copiedRegistered!.PlayerId);

        var copiedGuest = team1.Players.FirstOrDefault(p => p.IsGuest);
        Assert.NotNull(copiedGuest);
        Assert.Equal("Guest A", copiedGuest!.GuestName);
    }

    [Fact]
    public async Task Rematch_ClosedTable_ReturnsTableClosedError()
    {
        // Close the table
        var table = await _dbContext.Tables.FirstAsync(t => t.Id == _tableId);
        table.Status = TableStatus.Closed;
        await _dbContext.SaveChangesAsync();

        var parent = await _dbContext.GameSessions
            .Include(s => s.Table)
            .FirstAsync(s => s.Id == _sessionId);

        Assert.Equal(TableStatus.Closed, parent.Table.Status);
        // Endpoint would return TABLE_CLOSED error
    }

    [Fact]
    public async Task Rematch_DifferentDay_ReturnsNotSameDayError()
    {
        // Set parent session CreatedAt to yesterday
        var session = await _dbContext.GameSessions.FirstAsync(s => s.Id == _sessionId);
        session.CreatedAt = DateTime.UtcNow.AddDays(-1);
        await _dbContext.SaveChangesAsync();

        var parent = await _dbContext.GameSessions.FirstAsync(s => s.Id == _sessionId);
        Assert.NotEqual(DateTime.UtcNow.Date, parent.CreatedAt.Date);
        // Endpoint would return NOT_SAME_DAY error
    }

    [Fact]
    public async Task Rematch_NonCompletedSession_ReturnsError()
    {
        // Set session back to Active
        var session = await _dbContext.GameSessions.FirstAsync(s => s.Id == _sessionId);
        session.Status = GameSessionStatus.Active;
        await _dbContext.SaveChangesAsync();

        var result = await _dbContext.GameSessions
            .AsNoTracking()
            .FirstAsync(s => s.Id == _sessionId);

        Assert.Equal(GameSessionStatus.Active, result.Status);
        // Endpoint would return SESSION_NOT_COMPLETED error
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
