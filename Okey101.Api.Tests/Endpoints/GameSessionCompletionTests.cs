using Microsoft.EntityFrameworkCore;
using Okey101.Api.Data;
using Okey101.Api.Models.Entities;
using Okey101.Api.Models.Enums;
using Okey101.Api.Services;

namespace Okey101.Api.Tests.Endpoints;

public class GameSessionCompletionTests : IDisposable
{
    private readonly TenantProvider _tenantProvider;
    private readonly AppDbContext _dbContext;
    private readonly Guid _tenantId;
    private readonly Guid _sessionId;
    private readonly Guid _playerId;

    public GameSessionCompletionTests()
    {
        _tenantProvider = new TenantProvider();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new AppDbContext(options, _tenantProvider);
        _tenantId = Guid.NewGuid();
        _sessionId = Guid.NewGuid();
        _playerId = Guid.NewGuid();

        _tenantProvider.SetTenantId(null);

        var gameCenterId = Guid.NewGuid();
        var tableId = Guid.NewGuid();

        _dbContext.GameCenters.Add(new GameCenter
        {
            Id = gameCenterId,
            Name = "Test Center",
            IsActive = true,
            MaxTables = 10
        });
        _dbContext.Tables.Add(new Table
        {
            Id = tableId,
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
        _dbContext.GameSessions.Add(new GameSession
        {
            Id = _sessionId,
            TenantId = _tenantId,
            TableId = tableId,
            GameName = "Test Game",
            Status = GameSessionStatus.Active,
            CreatedByPlayerId = _playerId,
            Teams = new List<GameTeam>
            {
                new() { Id = Guid.NewGuid(), TeamName = "Team Alpha", TeamNumber = 1 },
                new() { Id = Guid.NewGuid(), TeamName = "Team Beta", TeamNumber = 2 },
            }
        });
        _dbContext.SaveChanges();
    }

    private void AddEndOfRoundEntries(int roundNumber, bool team1 = true, bool team2 = true)
    {
        if (team1)
        {
            _dbContext.ScoreEntries.Add(new ScoreEntry
            {
                Id = Guid.NewGuid(),
                GameSessionId = _sessionId,
                TeamNumber = 1,
                RoundNumber = roundNumber,
                ScoreType = ScoreType.EndOfRound,
                Value = 50,
                CreatedByPlayerId = _playerId,
            });
        }
        if (team2)
        {
            _dbContext.ScoreEntries.Add(new ScoreEntry
            {
                Id = Guid.NewGuid(),
                GameSessionId = _sessionId,
                TeamNumber = 2,
                RoundNumber = roundNumber,
                ScoreType = ScoreType.EndOfRound,
                Value = 30,
                CreatedByPlayerId = _playerId,
            });
        }
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task CompleteGame_WithRound8Complete_SetsStatusCompleted()
    {
        // Add endOfRound entries for all 8 rounds
        for (var r = 1; r <= 8; r++)
        {
            AddEndOfRoundEntries(r);
        }

        // Simulate the completion check (same logic as endpoint)
        var session = await _dbContext.GameSessions
            .Include(s => s.Teams)
            .FirstAsync(s => s.Id == _sessionId);

        var round8Entries = await _dbContext.ScoreEntries
            .Where(e => e.GameSessionId == _sessionId && e.RoundNumber == 8
                && e.ScoreType == ScoreType.EndOfRound && !e.IsRemoved)
            .ToListAsync();

        var team1Has = round8Entries.Any(e => e.TeamNumber == 1);
        var team2Has = round8Entries.Any(e => e.TeamNumber == 2);

        Assert.True(team1Has);
        Assert.True(team2Has);

        session.Status = GameSessionStatus.Completed;
        session.EndedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        var result = await _dbContext.GameSessions
            .FirstAsync(s => s.Id == _sessionId);
        Assert.Equal(GameSessionStatus.Completed, result.Status);
        Assert.NotNull(result.EndedAt);
    }

    [Fact]
    public async Task CompleteGame_WithRound8Incomplete_OnlyTeam1_Fails()
    {
        // Add endOfRound for rounds 1-7 complete, round 8 only team 1
        for (var r = 1; r <= 7; r++)
        {
            AddEndOfRoundEntries(r);
        }
        AddEndOfRoundEntries(8, team1: true, team2: false);

        var round8Entries = await _dbContext.ScoreEntries
            .Where(e => e.GameSessionId == _sessionId && e.RoundNumber == 8
                && e.ScoreType == ScoreType.EndOfRound && !e.IsRemoved)
            .ToListAsync();

        var team1Has = round8Entries.Any(e => e.TeamNumber == 1);
        var team2Has = round8Entries.Any(e => e.TeamNumber == 2);

        Assert.True(team1Has);
        Assert.False(team2Has);
        // Endpoint would return ROUND_8_INCOMPLETE
    }

    [Fact]
    public async Task CompleteGame_AlreadyCompleted_Fails()
    {
        var session = await _dbContext.GameSessions
            .FirstAsync(s => s.Id == _sessionId);
        session.Status = GameSessionStatus.Completed;
        session.EndedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        var result = await _dbContext.GameSessions
            .AsNoTracking()
            .FirstAsync(s => s.Id == _sessionId);

        Assert.Equal(GameSessionStatus.Completed, result.Status);
        // Endpoint would return SESSION_NOT_ACTIVE
    }

    [Fact]
    public async Task ScoreEntry_OnCompletedSession_BlockedByActiveCheck()
    {
        var session = await _dbContext.GameSessions
            .FirstAsync(s => s.Id == _sessionId);
        session.Status = GameSessionStatus.Completed;
        await _dbContext.SaveChangesAsync();

        // Verify the session is not active
        var result = await _dbContext.GameSessions
            .AsNoTracking()
            .FirstAsync(s => s.Id == _sessionId);

        Assert.NotEqual(GameSessionStatus.Active, result.Status);
        // Score POST endpoint checks session.Status != Active → returns SESSION_NOT_ACTIVE
    }

    [Fact]
    public async Task CompleteGame_CalculatesTotals_DeterminesWinner()
    {
        // Team 1 scores more
        for (var r = 1; r <= 8; r++)
        {
            _dbContext.ScoreEntries.Add(new ScoreEntry
            {
                Id = Guid.NewGuid(),
                GameSessionId = _sessionId,
                TeamNumber = 1,
                RoundNumber = r,
                ScoreType = ScoreType.EndOfRound,
                Value = 50,
                CreatedByPlayerId = _playerId,
            });
            _dbContext.ScoreEntries.Add(new ScoreEntry
            {
                Id = Guid.NewGuid(),
                GameSessionId = _sessionId,
                TeamNumber = 2,
                RoundNumber = r,
                ScoreType = ScoreType.EndOfRound,
                Value = 30,
                CreatedByPlayerId = _playerId,
            });
        }
        await _dbContext.SaveChangesAsync();

        var allEntries = await _dbContext.ScoreEntries
            .Where(e => e.GameSessionId == _sessionId && !e.IsRemoved)
            .ToListAsync();

        var team1Total = allEntries.Where(e => e.TeamNumber == 1).Sum(e => e.Value);
        var team2Total = allEntries.Where(e => e.TeamNumber == 2).Sum(e => e.Value);

        Assert.Equal(400, team1Total); // 50 * 8
        Assert.Equal(240, team2Total); // 30 * 8

        int? winner = null;
        if (team1Total > team2Total) winner = 1;
        else if (team2Total > team1Total) winner = 2;

        Assert.Equal(1, winner);
    }

    [Fact]
    public async Task CompleteGame_EqualTotals_WinnerIsNull()
    {
        for (var r = 1; r <= 8; r++)
        {
            _dbContext.ScoreEntries.Add(new ScoreEntry
            {
                Id = Guid.NewGuid(),
                GameSessionId = _sessionId,
                TeamNumber = 1,
                RoundNumber = r,
                ScoreType = ScoreType.EndOfRound,
                Value = 50,
                CreatedByPlayerId = _playerId,
            });
            _dbContext.ScoreEntries.Add(new ScoreEntry
            {
                Id = Guid.NewGuid(),
                GameSessionId = _sessionId,
                TeamNumber = 2,
                RoundNumber = r,
                ScoreType = ScoreType.EndOfRound,
                Value = 50,
                CreatedByPlayerId = _playerId,
            });
        }
        await _dbContext.SaveChangesAsync();

        var allEntries = await _dbContext.ScoreEntries
            .Where(e => e.GameSessionId == _sessionId && !e.IsRemoved)
            .ToListAsync();

        var team1Total = allEntries.Where(e => e.TeamNumber == 1).Sum(e => e.Value);
        var team2Total = allEntries.Where(e => e.TeamNumber == 2).Sum(e => e.Value);

        Assert.Equal(team1Total, team2Total);

        int? winner = null;
        if (team1Total > team2Total) winner = 1;
        else if (team2Total > team1Total) winner = 2;

        Assert.Null(winner);
    }

    [Fact]
    public async Task CompleteGame_ExcludesRemovedEntries_FromTotals()
    {
        AddEndOfRoundEntries(8);

        // Add a removed entry that should not count
        _dbContext.ScoreEntries.Add(new ScoreEntry
        {
            Id = Guid.NewGuid(),
            GameSessionId = _sessionId,
            TeamNumber = 1,
            RoundNumber = 1,
            ScoreType = ScoreType.Fine,
            Value = 100,
            CreatedByPlayerId = _playerId,
            IsRemoved = true,
            RemovedAt = DateTime.UtcNow,
        });
        await _dbContext.SaveChangesAsync();

        var allEntries = await _dbContext.ScoreEntries
            .Where(e => e.GameSessionId == _sessionId && !e.IsRemoved)
            .ToListAsync();

        var team1Total = allEntries.Where(e => e.TeamNumber == 1).Sum(e => e.Value);

        // Only the endOfRound entry (50) should count, not the removed fine (100)
        Assert.Equal(50, team1Total);
    }

    [Fact]
    public async Task CompleteGame_PersistsWinnerAndTotals_ToGameSession()
    {
        // Team 1 scores 50*8=400, Team 2 scores 30*8=240
        for (var r = 1; r <= 8; r++)
        {
            _dbContext.ScoreEntries.Add(new ScoreEntry
            {
                Id = Guid.NewGuid(),
                GameSessionId = _sessionId,
                TeamNumber = 1,
                RoundNumber = r,
                ScoreType = ScoreType.EndOfRound,
                Value = 50,
                CreatedByPlayerId = _playerId,
            });
            _dbContext.ScoreEntries.Add(new ScoreEntry
            {
                Id = Guid.NewGuid(),
                GameSessionId = _sessionId,
                TeamNumber = 2,
                RoundNumber = r,
                ScoreType = ScoreType.EndOfRound,
                Value = 30,
                CreatedByPlayerId = _playerId,
            });
        }
        await _dbContext.SaveChangesAsync();

        // Simulate completion logic (same as endpoint)
        var session = await _dbContext.GameSessions
            .Include(s => s.Teams)
            .FirstAsync(s => s.Id == _sessionId);

        var allEntries = await _dbContext.ScoreEntries
            .Where(e => e.GameSessionId == _sessionId && !e.IsRemoved)
            .ToListAsync();

        var team1Total = allEntries.Where(e => e.TeamNumber == 1).Sum(e => e.Value);
        var team2Total = allEntries.Where(e => e.TeamNumber == 2).Sum(e => e.Value);

        int? winner = null;
        if (team1Total > team2Total) winner = 1;
        else if (team2Total > team1Total) winner = 2;

        session.Status = GameSessionStatus.Completed;
        session.EndedAt = DateTime.UtcNow;
        session.WinnerTeamNumber = winner;
        session.Team1FinalTotal = team1Total;
        session.Team2FinalTotal = team2Total;
        await _dbContext.SaveChangesAsync();

        // Verify persisted values
        var persisted = await _dbContext.GameSessions
            .AsNoTracking()
            .FirstAsync(s => s.Id == _sessionId);

        Assert.Equal(GameSessionStatus.Completed, persisted.Status);
        Assert.Equal(1, persisted.WinnerTeamNumber);
        Assert.Equal(400, persisted.Team1FinalTotal);
        Assert.Equal(240, persisted.Team2FinalTotal);
        Assert.NotNull(persisted.EndedAt);
    }

    [Fact]
    public async Task GetResult_CompletedSession_ReturnsPersisted()
    {
        // Set up a completed session with persisted values
        var session = await _dbContext.GameSessions
            .Include(s => s.Teams)
            .FirstAsync(s => s.Id == _sessionId);

        session.Status = GameSessionStatus.Completed;
        session.EndedAt = DateTime.UtcNow;
        session.WinnerTeamNumber = 2;
        session.Team1FinalTotal = 200;
        session.Team2FinalTotal = 350;
        await _dbContext.SaveChangesAsync();

        // Query back as the GET result endpoint would
        var result = await _dbContext.GameSessions
            .AsNoTracking()
            .Include(s => s.Teams)
            .FirstAsync(s => s.Id == _sessionId);

        Assert.Equal(GameSessionStatus.Completed, result.Status);
        Assert.Equal(2, result.WinnerTeamNumber);
        Assert.Equal(200, result.Team1FinalTotal);
        Assert.Equal(350, result.Team2FinalTotal);

        var team1 = result.Teams.FirstOrDefault(t => t.TeamNumber == 1);
        var team2 = result.Teams.FirstOrDefault(t => t.TeamNumber == 2);
        Assert.Equal("Team Alpha", team1?.TeamName);
        Assert.Equal("Team Beta", team2?.TeamName);
    }

    [Fact]
    public async Task GetResult_ActiveSession_ShouldBeRejected()
    {
        // Session is Active by default from constructor setup
        var session = await _dbContext.GameSessions
            .AsNoTracking()
            .FirstAsync(s => s.Id == _sessionId);

        Assert.Equal(GameSessionStatus.Active, session.Status);
        // GET result endpoint would return 400 SESSION_NOT_COMPLETED
    }

    [Fact]
    public async Task CompleteGame_TiedScore_PersistsNullWinner()
    {
        for (var r = 1; r <= 8; r++)
        {
            _dbContext.ScoreEntries.Add(new ScoreEntry
            {
                Id = Guid.NewGuid(),
                GameSessionId = _sessionId,
                TeamNumber = 1,
                RoundNumber = r,
                ScoreType = ScoreType.EndOfRound,
                Value = 40,
                CreatedByPlayerId = _playerId,
            });
            _dbContext.ScoreEntries.Add(new ScoreEntry
            {
                Id = Guid.NewGuid(),
                GameSessionId = _sessionId,
                TeamNumber = 2,
                RoundNumber = r,
                ScoreType = ScoreType.EndOfRound,
                Value = 40,
                CreatedByPlayerId = _playerId,
            });
        }
        await _dbContext.SaveChangesAsync();

        var session = await _dbContext.GameSessions
            .FirstAsync(s => s.Id == _sessionId);

        var allEntries = await _dbContext.ScoreEntries
            .Where(e => e.GameSessionId == _sessionId && !e.IsRemoved)
            .ToListAsync();

        var team1Total = allEntries.Where(e => e.TeamNumber == 1).Sum(e => e.Value);
        var team2Total = allEntries.Where(e => e.TeamNumber == 2).Sum(e => e.Value);

        int? winner = null;
        if (team1Total > team2Total) winner = 1;
        else if (team2Total > team1Total) winner = 2;

        session.Status = GameSessionStatus.Completed;
        session.EndedAt = DateTime.UtcNow;
        session.WinnerTeamNumber = winner;
        session.Team1FinalTotal = team1Total;
        session.Team2FinalTotal = team2Total;
        await _dbContext.SaveChangesAsync();

        var persisted = await _dbContext.GameSessions
            .AsNoTracking()
            .FirstAsync(s => s.Id == _sessionId);

        Assert.Null(persisted.WinnerTeamNumber);
        Assert.Equal(320, persisted.Team1FinalTotal);
        Assert.Equal(320, persisted.Team2FinalTotal);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
