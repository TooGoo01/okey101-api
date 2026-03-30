using Microsoft.EntityFrameworkCore;
using Okey101.Api.Data;
using Okey101.Api.Models.Entities;
using Okey101.Api.Models.Enums;
using Okey101.Api.Services;

namespace Okey101.Api.Tests.Endpoints;

public class PlayerEndpointsTests : IDisposable
{
    private readonly TenantProvider _tenantProvider;
    private readonly AppDbContext _dbContext;
    private readonly Guid _gameCenterId;
    private readonly Guid _tenantId;
    private readonly Guid _tableId;
    private readonly Guid _playerId;

    public PlayerEndpointsTests()
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

    private GameSession CreateCompletedSessionWithGuest(string guestName, Guid? linkedPlayerId = null)
    {
        var session = new GameSession
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            TableId = _tableId,
            GameName = "Test Game",
            Status = GameSessionStatus.Completed,
            CreatedByPlayerId = _playerId,
            EndedAt = DateTime.UtcNow
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
            IsGuest = true,
            GuestName = guestName,
            PlayerId = linkedPlayerId
        };

        _dbContext.GameSessions.Add(session);
        _dbContext.GameTeams.Add(team);
        _dbContext.Set<GamePlayer>().Add(guestPlayer);
        _dbContext.SaveChanges();

        return session;
    }

    [Fact]
    public async Task GuestMatches_ReturnsMatchingGuestEntries()
    {
        _tenantProvider.SetTenantId(null);
        CreateCompletedSessionWithGuest("Test Player");
        CreateCompletedSessionWithGuest("Test Player");

        var matches = await _dbContext.Set<GamePlayer>()
            .IgnoreQueryFilters()
            .Where(gp => gp.IsGuest && gp.PlayerId == null
                && gp.GuestName != null
                && gp.GuestName.ToLower() == "test player")
            .Where(gp => gp.GameTeam.GameSession.Status == GameSessionStatus.Completed)
            .ToListAsync();

        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public async Task GuestMatches_ReturnsEmptyWhenNoMatches()
    {
        _tenantProvider.SetTenantId(null);
        CreateCompletedSessionWithGuest("Other Player");

        var matches = await _dbContext.Set<GamePlayer>()
            .IgnoreQueryFilters()
            .Where(gp => gp.IsGuest && gp.PlayerId == null
                && gp.GuestName != null
                && gp.GuestName.ToLower() == "test player")
            .Where(gp => gp.GameTeam.GameSession.Status == GameSessionStatus.Completed)
            .ToListAsync();

        Assert.Empty(matches);
    }

    [Fact]
    public async Task GuestMatches_DoesNotReturnAlreadyLinkedEntries()
    {
        _tenantProvider.SetTenantId(null);
        var linkedPlayerId = Guid.NewGuid();
        _dbContext.Players.Add(new Player
        {
            Id = linkedPlayerId,
            Name = "Test Player",
            PhoneNumber = "enc2",
            PhoneNumberHash = "hash456"
        });
        _dbContext.SaveChanges();

        CreateCompletedSessionWithGuest("Test Player", linkedPlayerId);
        CreateCompletedSessionWithGuest("Test Player");

        var matches = await _dbContext.Set<GamePlayer>()
            .IgnoreQueryFilters()
            .Where(gp => gp.IsGuest && gp.PlayerId == null
                && gp.GuestName != null
                && gp.GuestName.ToLower() == "test player")
            .Where(gp => gp.GameTeam.GameSession.Status == GameSessionStatus.Completed)
            .ToListAsync();

        Assert.Single(matches);
    }

    [Fact]
    public async Task LinkGuestHistory_UpdatesPlayerIdOnSelectedRecords()
    {
        _tenantProvider.SetTenantId(null);
        CreateCompletedSessionWithGuest("Test Player");
        CreateCompletedSessionWithGuest("Test Player");

        var guestPlayers = await _dbContext.Set<GamePlayer>()
            .IgnoreQueryFilters()
            .Where(gp => gp.IsGuest && gp.PlayerId == null && gp.GuestName == "Test Player")
            .ToListAsync();

        Assert.Equal(2, guestPlayers.Count);

        foreach (var gp in guestPlayers)
        {
            gp.PlayerId = _playerId;
        }
        await _dbContext.SaveChangesAsync();

        var linkedPlayers = await _dbContext.Set<GamePlayer>()
            .IgnoreQueryFilters()
            .Where(gp => gp.PlayerId == _playerId && gp.IsGuest)
            .ToListAsync();

        Assert.Equal(2, linkedPlayers.Count);
        Assert.All(linkedPlayers, gp =>
        {
            Assert.True(gp.IsGuest);
            Assert.Equal("Test Player", gp.GuestName);
        });
    }

    [Fact]
    public async Task LinkGuestHistory_RejectsAlreadyLinkedEntries()
    {
        _tenantProvider.SetTenantId(null);
        var otherPlayerId = Guid.NewGuid();
        _dbContext.Players.Add(new Player
        {
            Id = otherPlayerId,
            Name = "Other",
            PhoneNumber = "enc3",
            PhoneNumberHash = "hash789"
        });
        _dbContext.SaveChanges();

        CreateCompletedSessionWithGuest("Test Player", otherPlayerId);

        var alreadyLinked = await _dbContext.Set<GamePlayer>()
            .IgnoreQueryFilters()
            .Where(gp => gp.IsGuest && gp.PlayerId != null && gp.GuestName == "Test Player")
            .ToListAsync();

        Assert.Single(alreadyLinked);
        Assert.NotNull(alreadyLinked[0].PlayerId);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
