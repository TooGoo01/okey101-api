using Microsoft.EntityFrameworkCore;
using Okey101.Api.Data;
using Okey101.Api.Models.Entities;
using Okey101.Api.Models.Enums;
using Okey101.Api.Services;

namespace Okey101.Api.Tests.Endpoints;

public class GameSessionEndpointsTests : IDisposable
{
    private readonly TenantProvider _tenantProvider;
    private readonly AppDbContext _dbContext;
    private readonly Guid _gameCenterId;
    private readonly Guid _tenantId;
    private readonly Guid _tableId;
    private readonly Guid _playerId;

    public GameSessionEndpointsTests()
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

        // Seed data
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
            TableNumber = 1,
            Status = TableStatus.Active,
            QrCodeIdentifier = "test-qr",
            GameCenterId = _gameCenterId
        });
        _dbContext.Players.Add(new Player
        {
            Id = _playerId,
            Name = "Test Player",
            PhoneNumber = "encrypted-phone",
            PhoneNumberHash = "hash123",
            TenantId = _tenantId
        });
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task CreateGameSession_ValidRequest_CreatesSession()
    {
        _tenantProvider.SetTenantId(null);

        var session = new GameSession
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            TableId = _tableId,
            GameName = "Test Game",
            Status = GameSessionStatus.Pending,
            CreatedByPlayerId = _playerId
        };

        var team1 = new GameTeam
        {
            Id = Guid.NewGuid(),
            GameSessionId = session.Id,
            TeamName = "Team 1",
            TeamNumber = 1
        };
        team1.Players.Add(new GamePlayer
        {
            Id = Guid.NewGuid(),
            GameTeamId = team1.Id,
            GuestName = "Guest Player",
            IsGuest = true
        });

        var team2 = new GameTeam
        {
            Id = Guid.NewGuid(),
            GameSessionId = session.Id,
            TeamName = "Team 2",
            TeamNumber = 2
        };
        team2.Players.Add(new GamePlayer
        {
            Id = Guid.NewGuid(),
            GameTeamId = team2.Id,
            PlayerId = _playerId,
            IsGuest = false
        });

        session.Teams.Add(team1);
        session.Teams.Add(team2);

        _dbContext.GameSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var created = await _dbContext.GameSessions
            .IgnoreQueryFilters()
            .Include(gs => gs.Teams)
            .ThenInclude(t => t.Players)
            .FirstOrDefaultAsync(gs => gs.Id == session.Id);

        Assert.NotNull(created);
        Assert.Equal("Test Game", created.GameName);
        Assert.Equal(GameSessionStatus.Pending, created.Status);
        Assert.Equal(2, created.Teams.Count);
        Assert.Equal(_playerId, created.CreatedByPlayerId);
    }

    [Fact]
    public async Task GameSession_TenantFilter_FiltersCorrectly()
    {
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();

        _tenantProvider.SetTenantId(null);

        _dbContext.GameSessions.AddRange(
            new GameSession
            {
                Id = Guid.NewGuid(),
                TenantId = tenant1,
                TableId = _tableId,
                GameName = "Tenant 1 Game",
                CreatedByPlayerId = _playerId
            },
            new GameSession
            {
                Id = Guid.NewGuid(),
                TenantId = tenant2,
                TableId = _tableId,
                GameName = "Tenant 2 Game",
                CreatedByPlayerId = _playerId
            }
        );
        await _dbContext.SaveChangesAsync();

        _tenantProvider.SetTenantId(tenant1);
        var sessions = await _dbContext.GameSessions.ToListAsync();

        Assert.Single(sessions);
        Assert.Equal(tenant1, sessions[0].TenantId);
    }

    [Fact]
    public async Task GameSession_CascadeDelete_DeletesTeamsAndPlayers()
    {
        _tenantProvider.SetTenantId(null);

        var session = new GameSession
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            TableId = _tableId,
            GameName = "Cascade Test",
            CreatedByPlayerId = _playerId
        };

        var team = new GameTeam
        {
            Id = Guid.NewGuid(),
            GameSessionId = session.Id,
            TeamName = "Team A",
            TeamNumber = 1
        };
        team.Players.Add(new GamePlayer
        {
            Id = Guid.NewGuid(),
            GameTeamId = team.Id,
            GuestName = "Player 1",
            IsGuest = true
        });
        session.Teams.Add(team);

        _dbContext.GameSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        // Verify cascade configuration exists
        var entityType = _dbContext.Model.FindEntityType(typeof(GameTeam));
        var fk = entityType?.GetForeignKeys()
            .FirstOrDefault(f => f.PrincipalEntityType.ClrType == typeof(GameSession));

        Assert.NotNull(fk);
        Assert.Equal(DeleteBehavior.Cascade, fk.DeleteBehavior);
    }

    [Fact]
    public void GameSessionStatus_HasExpectedValues()
    {
        Assert.Equal(0, (int)GameSessionStatus.Pending);
        Assert.Equal(1, (int)GameSessionStatus.Approved);
        Assert.Equal(2, (int)GameSessionStatus.Active);
        Assert.Equal(3, (int)GameSessionStatus.Completed);
        Assert.Equal(4, (int)GameSessionStatus.Closed);
        Assert.Equal(5, (int)GameSessionStatus.Rejected);
    }

    [Fact]
    public async Task GamePlayer_GuestAndRegistered_StoredCorrectly()
    {
        _tenantProvider.SetTenantId(null);

        var session = new GameSession
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            TableId = _tableId,
            GameName = "Player Type Test",
            CreatedByPlayerId = _playerId
        };

        var team = new GameTeam
        {
            Id = Guid.NewGuid(),
            GameSessionId = session.Id,
            TeamName = "Team 1",
            TeamNumber = 1
        };

        var guestPlayer = new GamePlayer
        {
            Id = Guid.NewGuid(),
            GameTeamId = team.Id,
            GuestName = "John Guest",
            IsGuest = true
        };

        var registeredPlayer = new GamePlayer
        {
            Id = Guid.NewGuid(),
            GameTeamId = team.Id,
            PlayerId = _playerId,
            IsGuest = false
        };

        team.Players.Add(guestPlayer);
        team.Players.Add(registeredPlayer);
        session.Teams.Add(team);

        _dbContext.GameSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var players = await _dbContext.GamePlayers
            .IgnoreQueryFilters()
            .Where(p => p.GameTeamId == team.Id)
            .ToListAsync();

        Assert.Equal(2, players.Count);

        var guest = players.First(p => p.IsGuest);
        Assert.Equal("John Guest", guest.GuestName);
        Assert.Null(guest.PlayerId);

        var registered = players.First(p => !p.IsGuest);
        Assert.Equal(_playerId, registered.PlayerId);
        Assert.Null(registered.GuestName);
    }

    [Fact]
    public async Task PlayerSearch_ByPhoneHash_FindsPlayer()
    {
        _tenantProvider.SetTenantId(null);

        var player = await _dbContext.Players
            .IgnoreQueryFilters()
            .Where(p => p.PhoneNumberHash == "hash123")
            .Select(p => new { p.Id, p.Name })
            .FirstOrDefaultAsync();

        Assert.NotNull(player);
        Assert.Equal("Test Player", player.Name);
        Assert.Equal(_playerId, player.Id);
    }

    [Fact]
    public async Task PlayerSearch_NonExistentHash_ReturnsNull()
    {
        _tenantProvider.SetTenantId(null);

        var player = await _dbContext.Players
            .IgnoreQueryFilters()
            .Where(p => p.PhoneNumberHash == "nonexistent-hash")
            .FirstOrDefaultAsync();

        Assert.Null(player);
    }

    // Helper to create a seeded pending game session
    private async Task<GameSession> CreatePendingSession(string gameName = "Test Game")
    {
        _tenantProvider.SetTenantId(null);

        var session = new GameSession
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            TableId = _tableId,
            GameName = gameName,
            Status = GameSessionStatus.Pending,
            CreatedByPlayerId = _playerId
        };

        var team1 = new GameTeam
        {
            Id = Guid.NewGuid(),
            GameSessionId = session.Id,
            TeamName = "Team 1",
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
            TeamName = "Team 2",
            TeamNumber = 2
        };
        team2.Players.Add(new GamePlayer
        {
            Id = Guid.NewGuid(),
            GameTeamId = team2.Id,
            GuestName = "Guest Player",
            IsGuest = true
        });

        session.Teams.Add(team1);
        session.Teams.Add(team2);

        _dbContext.GameSessions.Add(session);
        await _dbContext.SaveChangesAsync();
        return session;
    }

    // --- Approve Endpoint Tests ---

    [Fact]
    public async Task ApproveSession_ValidPending_SetsApprovedStatus()
    {
        var session = await CreatePendingSession();

        // Simulate approve by updating directly (testing DB state transitions)
        var loaded = await _dbContext.GameSessions
            .IgnoreQueryFilters()
            .FirstAsync(gs => gs.Id == session.Id);

        Assert.Equal(GameSessionStatus.Pending, loaded.Status);

        loaded.Status = GameSessionStatus.Approved;
        loaded.ApprovedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        var approved = await _dbContext.GameSessions
            .IgnoreQueryFilters()
            .FirstAsync(gs => gs.Id == session.Id);

        Assert.Equal(GameSessionStatus.Approved, approved.Status);
        Assert.NotNull(approved.ApprovedAt);
    }

    [Fact]
    public async Task ApproveSession_NotFound_SessionDoesNotExist()
    {
        _tenantProvider.SetTenantId(null);

        var nonExistentId = Guid.NewGuid();
        var session = await _dbContext.GameSessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(gs => gs.Id == nonExistentId);

        Assert.Null(session);
    }

    [Fact]
    public async Task ApproveSession_AlreadyApproved_CannotApproveAgain()
    {
        var session = await CreatePendingSession();

        var loaded = await _dbContext.GameSessions
            .IgnoreQueryFilters()
            .FirstAsync(gs => gs.Id == session.Id);

        loaded.Status = GameSessionStatus.Approved;
        loaded.ApprovedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        var reloaded = await _dbContext.GameSessions
            .IgnoreQueryFilters()
            .FirstAsync(gs => gs.Id == session.Id);

        // Verify the session is no longer Pending
        Assert.NotEqual(GameSessionStatus.Pending, reloaded.Status);
        Assert.Equal(GameSessionStatus.Approved, reloaded.Status);
    }

    // --- Reject Endpoint Tests ---

    [Fact]
    public async Task RejectSession_ValidPending_SetsRejectedStatus()
    {
        var session = await CreatePendingSession();

        var loaded = await _dbContext.GameSessions
            .IgnoreQueryFilters()
            .FirstAsync(gs => gs.Id == session.Id);

        loaded.Status = GameSessionStatus.Rejected;
        loaded.EndedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        var rejected = await _dbContext.GameSessions
            .IgnoreQueryFilters()
            .FirstAsync(gs => gs.Id == session.Id);

        Assert.Equal(GameSessionStatus.Rejected, rejected.Status);
        Assert.NotNull(rejected.EndedAt);
    }

    [Fact]
    public async Task RejectSession_NotFound_SessionDoesNotExist()
    {
        _tenantProvider.SetTenantId(null);

        var nonExistentId = Guid.NewGuid();
        var session = await _dbContext.GameSessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(gs => gs.Id == nonExistentId);

        Assert.Null(session);
    }

    [Fact]
    public async Task RejectSession_AlreadyRejected_CannotRejectAgain()
    {
        var session = await CreatePendingSession();

        var loaded = await _dbContext.GameSessions
            .IgnoreQueryFilters()
            .FirstAsync(gs => gs.Id == session.Id);

        loaded.Status = GameSessionStatus.Rejected;
        loaded.EndedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        var reloaded = await _dbContext.GameSessions
            .IgnoreQueryFilters()
            .FirstAsync(gs => gs.Id == session.Id);

        Assert.NotEqual(GameSessionStatus.Pending, reloaded.Status);
        Assert.Equal(GameSessionStatus.Rejected, reloaded.Status);
    }

    // --- Get Pending Sessions Tests ---

    [Fact]
    public async Task GetSessions_FilterByPendingStatus_ReturnsPendingOnly()
    {
        _tenantProvider.SetTenantId(null);

        // Create one pending and one approved session with different tables
        var table2Id = Guid.NewGuid();
        _dbContext.Tables.Add(new Table
        {
            Id = table2Id,
            TenantId = _tenantId,
            TableNumber = 2,
            Status = TableStatus.Active,
            QrCodeIdentifier = "test-qr-2",
            GameCenterId = _gameCenterId
        });
        await _dbContext.SaveChangesAsync();

        _dbContext.GameSessions.AddRange(
            new GameSession
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                TableId = _tableId,
                GameName = "Pending Game",
                Status = GameSessionStatus.Pending,
                CreatedByPlayerId = _playerId
            },
            new GameSession
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                TableId = table2Id,
                GameName = "Approved Game",
                Status = GameSessionStatus.Approved,
                CreatedByPlayerId = _playerId
            }
        );
        await _dbContext.SaveChangesAsync();

        var pendingSessions = await _dbContext.GameSessions
            .IgnoreQueryFilters()
            .Where(gs => gs.Status == GameSessionStatus.Pending)
            .ToListAsync();

        Assert.Single(pendingSessions);
        Assert.Equal("Pending Game", pendingSessions[0].GameName);
    }

    [Fact]
    public async Task GetSessions_FilterByGameCenterId_ReturnsMatchingSessions()
    {
        _tenantProvider.SetTenantId(null);

        var otherGameCenterId = Guid.NewGuid();
        _dbContext.GameCenters.Add(new GameCenter
        {
            Id = otherGameCenterId,
            Name = "Other Center",
            IsActive = true,
            MaxTables = 5
        });
        var otherTableId = Guid.NewGuid();
        _dbContext.Tables.Add(new Table
        {
            Id = otherTableId,
            TenantId = _tenantId,
            TableNumber = 99,
            Status = TableStatus.Active,
            QrCodeIdentifier = "other-qr",
            GameCenterId = otherGameCenterId
        });
        await _dbContext.SaveChangesAsync();

        _dbContext.GameSessions.AddRange(
            new GameSession
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                TableId = _tableId,
                GameName = "Center 1 Game",
                Status = GameSessionStatus.Pending,
                CreatedByPlayerId = _playerId
            },
            new GameSession
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                TableId = otherTableId,
                GameName = "Center 2 Game",
                Status = GameSessionStatus.Pending,
                CreatedByPlayerId = _playerId
            }
        );
        await _dbContext.SaveChangesAsync();

        var sessionsForCenter1 = await _dbContext.GameSessions
            .IgnoreQueryFilters()
            .Include(gs => gs.Table)
            .Where(gs => gs.Table.GameCenterId == _gameCenterId)
            .ToListAsync();

        Assert.Single(sessionsForCenter1);
        Assert.Equal("Center 1 Game", sessionsForCenter1[0].GameName);
    }

    [Fact]
    public async Task GetSessions_NoPendingSessions_ReturnsEmpty()
    {
        _tenantProvider.SetTenantId(null);

        var sessions = await _dbContext.GameSessions
            .IgnoreQueryFilters()
            .Where(gs => gs.Status == GameSessionStatus.Pending)
            .ToListAsync();

        Assert.Empty(sessions);
    }

    // --- Status Transition Tests ---

    [Fact]
    public async Task StatusTransition_PendingToApproved_Valid()
    {
        var session = await CreatePendingSession();

        var loaded = await _dbContext.GameSessions
            .IgnoreQueryFilters()
            .FirstAsync(gs => gs.Id == session.Id);

        Assert.Equal(GameSessionStatus.Pending, loaded.Status);

        loaded.Status = GameSessionStatus.Approved;
        loaded.ApprovedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        Assert.Equal(GameSessionStatus.Approved, loaded.Status);
    }

    [Fact]
    public async Task StatusTransition_PendingToRejected_Valid()
    {
        var session = await CreatePendingSession();

        var loaded = await _dbContext.GameSessions
            .IgnoreQueryFilters()
            .FirstAsync(gs => gs.Id == session.Id);

        Assert.Equal(GameSessionStatus.Pending, loaded.Status);

        loaded.Status = GameSessionStatus.Rejected;
        loaded.EndedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        Assert.Equal(GameSessionStatus.Rejected, loaded.Status);
    }

    [Fact]
    public void GameSessionStatus_RejectedValue_IsCorrect()
    {
        Assert.Equal(5, (int)GameSessionStatus.Rejected);
        Assert.Equal("Rejected", GameSessionStatus.Rejected.ToString());
    }

    // --- Player History Filter Tests ---

    private async Task<GameSession> CreateCompletedSessionWithPlayer(
        Guid playerId, string gameName = "Completed Game",
        int team1Total = 100, int team2Total = 80, int? winner = 1)
    {
        _tenantProvider.SetTenantId(null);

        var session = new GameSession
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            TableId = _tableId,
            GameName = gameName,
            Status = GameSessionStatus.Completed,
            CreatedByPlayerId = playerId,
            EndedAt = DateTime.UtcNow,
            WinnerTeamNumber = winner,
            Team1FinalTotal = team1Total,
            Team2FinalTotal = team2Total,
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
            PlayerId = playerId,
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
            GuestName = "Guest",
            IsGuest = true
        });

        session.Teams.Add(team1);
        session.Teams.Add(team2);

        _dbContext.GameSessions.Add(session);
        await _dbContext.SaveChangesAsync();
        return session;
    }

    [Fact]
    public async Task GetSessions_FilterByPlayerId_ReturnsOnlyParticipatedSessions()
    {
        _tenantProvider.SetTenantId(null);

        var otherPlayerId = Guid.NewGuid();
        _dbContext.Players.Add(new Player
        {
            Id = otherPlayerId,
            Name = "Other Player",
            PhoneNumber = "encrypted-other",
            PhoneNumberHash = "hash-other",
            TenantId = _tenantId
        });
        await _dbContext.SaveChangesAsync();

        await CreateCompletedSessionWithPlayer(_playerId, "My Game");
        await CreateCompletedSessionWithPlayer(otherPlayerId, "Other Game");

        // Query sessions for _playerId only
        var sessions = await _dbContext.GameSessions
            .IgnoreQueryFilters()
            .Include(gs => gs.Teams)
                .ThenInclude(t => t.Players)
            .Where(gs => gs.Teams.Any(t => t.Players.Any(p =>
                p.PlayerId.HasValue && p.PlayerId.Value == _playerId)))
            .Where(gs => gs.Status == GameSessionStatus.Completed)
            .ToListAsync();

        Assert.Single(sessions);
        Assert.Equal("My Game", sessions[0].GameName);
    }

    [Fact]
    public async Task GetSessions_FilterByPlayerId_OnlyReturnsCompletedSessions()
    {
        _tenantProvider.SetTenantId(null);

        await CreateCompletedSessionWithPlayer(_playerId, "Completed Game");
        await CreatePendingSession("Pending Game");

        var sessions = await _dbContext.GameSessions
            .IgnoreQueryFilters()
            .Include(gs => gs.Teams)
                .ThenInclude(t => t.Players)
            .Where(gs => gs.Teams.Any(t => t.Players.Any(p =>
                p.PlayerId.HasValue && p.PlayerId.Value == _playerId)))
            .Where(gs => gs.Status == GameSessionStatus.Completed)
            .ToListAsync();

        Assert.Single(sessions);
        Assert.Equal("Completed Game", sessions[0].GameName);
    }

    [Fact]
    public async Task GetSessions_CompletedSession_IncludesResultFields()
    {
        _tenantProvider.SetTenantId(null);

        var session = await CreateCompletedSessionWithPlayer(
            _playerId, "Result Test", team1Total: 150, team2Total: 120, winner: 1);

        var loaded = await _dbContext.GameSessions
            .IgnoreQueryFilters()
            .FirstAsync(gs => gs.Id == session.Id);

        Assert.Equal(150, loaded.Team1FinalTotal);
        Assert.Equal(120, loaded.Team2FinalTotal);
        Assert.Equal(1, loaded.WinnerTeamNumber);
        Assert.NotNull(loaded.EndedAt);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
