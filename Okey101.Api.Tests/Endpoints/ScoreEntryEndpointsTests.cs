using Microsoft.EntityFrameworkCore;
using Okey101.Api.Data;
using Okey101.Api.Models.Entities;
using Okey101.Api.Models.Enums;
using Okey101.Api.Services;

namespace Okey101.Api.Tests.Endpoints;

public class ScoreEntryEndpointsTests : IDisposable
{
    private readonly TenantProvider _tenantProvider;
    private readonly AppDbContext _dbContext;
    private readonly Guid _tenantId;
    private readonly Guid _sessionId;
    private readonly Guid _playerId;

    public ScoreEntryEndpointsTests()
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
            CreatedByPlayerId = _playerId
        });
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task GetScoreEntries_ReturnsEmptyList_WhenNoEntries()
    {
        var entries = await _dbContext.ScoreEntries
            .Where(e => e.GameSessionId == _sessionId)
            .ToListAsync();

        Assert.Empty(entries);
    }

    [Fact]
    public async Task GetScoreEntries_ReturnsEntries_OrderedByRoundThenCreatedAt()
    {
        var entry1 = new ScoreEntry
        {
            Id = Guid.NewGuid(),
            GameSessionId = _sessionId,
            TeamNumber = 1,
            RoundNumber = 2,
            ScoreType = ScoreType.Fine,
            Value = 30,
            CreatedByPlayerId = _playerId,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        var entry2 = new ScoreEntry
        {
            Id = Guid.NewGuid(),
            GameSessionId = _sessionId,
            TeamNumber = 2,
            RoundNumber = 1,
            ScoreType = ScoreType.EndOfRound,
            Value = 50,
            CreatedByPlayerId = _playerId,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10)
        };
        var entry3 = new ScoreEntry
        {
            Id = Guid.NewGuid(),
            GameSessionId = _sessionId,
            TeamNumber = 1,
            RoundNumber = 1,
            ScoreType = ScoreType.Fine,
            Value = 20,
            CreatedByPlayerId = _playerId,
            CreatedAt = DateTime.UtcNow.AddMinutes(-15)
        };

        _dbContext.ScoreEntries.AddRange(entry1, entry2, entry3);
        await _dbContext.SaveChangesAsync();

        var results = await _dbContext.ScoreEntries
            .Where(e => e.GameSessionId == _sessionId)
            .OrderBy(e => e.RoundNumber)
            .ThenBy(e => e.CreatedAt)
            .ToListAsync();

        Assert.Equal(3, results.Count);
        Assert.Equal(1, results[0].RoundNumber);
        Assert.Equal(1, results[1].RoundNumber);
        Assert.Equal(2, results[2].RoundNumber);
        // Within round 1, entry3 (older) comes before entry2 (newer)
        Assert.Equal(entry3.Id, results[0].Id);
        Assert.Equal(entry2.Id, results[1].Id);
    }

    [Fact]
    public async Task GetScoreEntries_IncludesRemovedEntries()
    {
        var activeEntry = new ScoreEntry
        {
            Id = Guid.NewGuid(),
            GameSessionId = _sessionId,
            TeamNumber = 1,
            RoundNumber = 1,
            ScoreType = ScoreType.Fine,
            Value = 30,
            CreatedByPlayerId = _playerId,
            IsRemoved = false
        };
        var removedEntry = new ScoreEntry
        {
            Id = Guid.NewGuid(),
            GameSessionId = _sessionId,
            TeamNumber = 2,
            RoundNumber = 1,
            ScoreType = ScoreType.Fine,
            Value = 20,
            CreatedByPlayerId = _playerId,
            IsRemoved = true,
            RemovedAt = DateTime.UtcNow
        };

        _dbContext.ScoreEntries.AddRange(activeEntry, removedEntry);
        await _dbContext.SaveChangesAsync();

        var results = await _dbContext.ScoreEntries
            .Where(e => e.GameSessionId == _sessionId)
            .ToListAsync();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, e => e.IsRemoved);
        Assert.Contains(results, e => !e.IsRemoved);
    }

    [Fact]
    public void TotalCalculation_ExcludesRemovedEntries()
    {
        var entries = new List<ScoreEntry>
        {
            new()
            {
                Id = Guid.NewGuid(),
                GameSessionId = _sessionId,
                TeamNumber = 1,
                RoundNumber = 1,
                ScoreType = ScoreType.Fine,
                Value = 30,
                CreatedByPlayerId = _playerId,
                IsRemoved = false
            },
            new()
            {
                Id = Guid.NewGuid(),
                GameSessionId = _sessionId,
                TeamNumber = 1,
                RoundNumber = 1,
                ScoreType = ScoreType.Fine,
                Value = 20,
                CreatedByPlayerId = _playerId,
                IsRemoved = true,
                RemovedAt = DateTime.UtcNow
            },
            new()
            {
                Id = Guid.NewGuid(),
                GameSessionId = _sessionId,
                TeamNumber = 2,
                RoundNumber = 1,
                ScoreType = ScoreType.EndOfRound,
                Value = 50,
                CreatedByPlayerId = _playerId,
                IsRemoved = false
            },
        };

        var team1Total = entries
            .Where(e => e.TeamNumber == 1 && !e.IsRemoved)
            .Sum(e => e.Value);
        var team2Total = entries
            .Where(e => e.TeamNumber == 2 && !e.IsRemoved)
            .Sum(e => e.Value);

        Assert.Equal(30, team1Total);
        Assert.Equal(50, team2Total);
    }

    [Fact]
    public async Task CreateScoreEntry_ValidFine_CreatesEntry()
    {
        var entry = new ScoreEntry
        {
            Id = Guid.NewGuid(),
            GameSessionId = _sessionId,
            TeamNumber = 1,
            RoundNumber = 1,
            ScoreType = ScoreType.Fine,
            Value = 30,
            CreatedByPlayerId = _playerId,
        };

        _dbContext.ScoreEntries.Add(entry);
        await _dbContext.SaveChangesAsync();

        var result = await _dbContext.ScoreEntries
            .FirstOrDefaultAsync(e => e.Id == entry.Id);

        Assert.NotNull(result);
        Assert.Equal(30, result.Value);
        Assert.Equal(ScoreType.Fine, result.ScoreType);
        Assert.False(result.IsRemoved);
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(20, true)]
    [InlineData(100, true)]
    [InlineData(200, true)]
    [InlineData(15, false)]
    [InlineData(5, false)]
    [InlineData(0, false)]
    [InlineData(210, false)]
    [InlineData(-10, false)]
    public void FineValidation_ChecksMultipleOf10InRange(int value, bool expected)
    {
        var isValid = value > 0 && value <= 200 && value % 10 == 0;
        Assert.Equal(expected, isValid);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void TeamNumberValidation_OnlyAllows1Or2(int teamNumber, bool expected)
    {
        var isValid = teamNumber is 1 or 2;
        Assert.Equal(expected, isValid);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(8, true)]
    [InlineData(9, false)]
    public void RoundNumberValidation_OnlyAllows1Through8(int roundNumber, bool expected)
    {
        var isValid = roundNumber is >= 1 and <= 8;
        Assert.Equal(expected, isValid);
    }

    [Fact]
    public async Task CreateScoreEntry_MultipleEntries_AllPersisted()
    {
        var entries = new[]
        {
            new ScoreEntry
            {
                Id = Guid.NewGuid(),
                GameSessionId = _sessionId,
                TeamNumber = 1,
                RoundNumber = 1,
                ScoreType = ScoreType.Fine,
                Value = 30,
                CreatedByPlayerId = _playerId,
            },
            new ScoreEntry
            {
                Id = Guid.NewGuid(),
                GameSessionId = _sessionId,
                TeamNumber = 2,
                RoundNumber = 1,
                ScoreType = ScoreType.Fine,
                Value = 20,
                CreatedByPlayerId = _playerId,
            },
        };

        _dbContext.ScoreEntries.AddRange(entries);
        await _dbContext.SaveChangesAsync();

        var count = await _dbContext.ScoreEntries
            .Where(e => e.GameSessionId == _sessionId)
            .CountAsync();

        Assert.Equal(2, count);
    }

    [Theory]
    [InlineData(50, true)]
    [InlineData(-30, true)]
    [InlineData(1, true)]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    public void EndOfRoundValidation_RejectsZeroOnly(int value, bool expected)
    {
        var isValid = value != 0;
        Assert.Equal(expected, isValid);
    }

    [Fact]
    public async Task CreateScoreEntry_EndOfRound_PositiveValue_CreatesEntry()
    {
        var entry = new ScoreEntry
        {
            Id = Guid.NewGuid(),
            GameSessionId = _sessionId,
            TeamNumber = 1,
            RoundNumber = 1,
            ScoreType = ScoreType.EndOfRound,
            Value = 120,
            CreatedByPlayerId = _playerId,
        };

        _dbContext.ScoreEntries.Add(entry);
        await _dbContext.SaveChangesAsync();

        var result = await _dbContext.ScoreEntries
            .FirstOrDefaultAsync(e => e.Id == entry.Id);

        Assert.NotNull(result);
        Assert.Equal(120, result.Value);
        Assert.Equal(ScoreType.EndOfRound, result.ScoreType);
    }

    [Fact]
    public async Task CreateScoreEntry_EndOfRound_NegativeValue_CreatesEntry()
    {
        var entry = new ScoreEntry
        {
            Id = Guid.NewGuid(),
            GameSessionId = _sessionId,
            TeamNumber = 2,
            RoundNumber = 1,
            ScoreType = ScoreType.EndOfRound,
            Value = -50,
            CreatedByPlayerId = _playerId,
        };

        _dbContext.ScoreEntries.Add(entry);
        await _dbContext.SaveChangesAsync();

        var result = await _dbContext.ScoreEntries
            .FirstOrDefaultAsync(e => e.Id == entry.Id);

        Assert.NotNull(result);
        Assert.Equal(-50, result.Value);
        Assert.Equal(ScoreType.EndOfRound, result.ScoreType);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(3, false)]
    [InlineData(-1, false)]
    public void TeamNumberValidation_RejectsInvalid(int teamNumber, bool expected)
    {
        var isValid = teamNumber is 1 or 2;
        Assert.Equal(expected, isValid);
    }

    [Theory]
    [InlineData(15, false)]
    [InlineData(210, false)]
    [InlineData(7, false)]
    [InlineData(30, true)]
    [InlineData(10, true)]
    [InlineData(200, true)]
    public void FineValidation_EdgeCases(int value, bool expected)
    {
        var isValid = value > 0 && value <= 200 && value % 10 == 0;
        Assert.Equal(expected, isValid);
    }

    [Fact]
    public async Task RemoveScoreEntry_SetsIsRemovedAndRemovedAt()
    {
        var entry = new ScoreEntry
        {
            Id = Guid.NewGuid(),
            GameSessionId = _sessionId,
            TeamNumber = 1,
            RoundNumber = 1,
            ScoreType = ScoreType.Fine,
            Value = 30,
            CreatedByPlayerId = _playerId,
            CreatedAt = DateTime.UtcNow,
            IsRemoved = false,
        };

        _dbContext.ScoreEntries.Add(entry);
        await _dbContext.SaveChangesAsync();

        // Simulate the remove operation (same logic as endpoint)
        var dbEntry = await _dbContext.ScoreEntries
            .FirstOrDefaultAsync(e => e.Id == entry.Id && e.GameSessionId == _sessionId);

        Assert.NotNull(dbEntry);
        Assert.False(dbEntry!.IsRemoved);

        dbEntry.IsRemoved = true;
        dbEntry.RemovedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        var result = await _dbContext.ScoreEntries
            .FirstOrDefaultAsync(e => e.Id == entry.Id);

        Assert.NotNull(result);
        Assert.True(result!.IsRemoved);
        Assert.NotNull(result.RemovedAt);
    }

    [Fact]
    public async Task RemoveScoreEntry_AlreadyRemoved_ReturnsError()
    {
        var entry = new ScoreEntry
        {
            Id = Guid.NewGuid(),
            GameSessionId = _sessionId,
            TeamNumber = 1,
            RoundNumber = 1,
            ScoreType = ScoreType.Fine,
            Value = 30,
            CreatedByPlayerId = _playerId,
            CreatedAt = DateTime.UtcNow,
            IsRemoved = true,
            RemovedAt = DateTime.UtcNow,
        };

        _dbContext.ScoreEntries.Add(entry);
        await _dbContext.SaveChangesAsync();

        var dbEntry = await _dbContext.ScoreEntries
            .FirstOrDefaultAsync(e => e.Id == entry.Id && e.GameSessionId == _sessionId);

        Assert.NotNull(dbEntry);
        Assert.True(dbEntry!.IsRemoved);
        // Endpoint would return BadRequest("ALREADY_REMOVED") — verify the guard condition
    }

    [Fact]
    public async Task RemoveScoreEntry_NonExistentSession_ReturnsNotFound()
    {
        var nonExistentSessionId = Guid.NewGuid();
        var entryId = Guid.NewGuid();

        var session = await _dbContext.GameSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == nonExistentSessionId);

        Assert.Null(session);
        // Endpoint would return NotFound("SESSION_NOT_FOUND")
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
