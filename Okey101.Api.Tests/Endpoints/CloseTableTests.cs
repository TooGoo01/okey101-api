using Microsoft.EntityFrameworkCore;
using Okey101.Api.Data;
using Okey101.Api.Models.Entities;
using Okey101.Api.Models.Enums;
using Okey101.Api.Services;

namespace Okey101.Api.Tests.Endpoints;

public class CloseTableTests : IDisposable
{
    private readonly TenantProvider _tenantProvider;
    private readonly AppDbContext _dbContext;
    private readonly Guid _gameCenterId;
    private readonly Guid _tenantId;
    private readonly Guid _playerId;

    public CloseTableTests()
    {
        _tenantProvider = new TenantProvider();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new AppDbContext(options, _tenantProvider);
        _gameCenterId = Guid.NewGuid();
        _tenantId = Guid.NewGuid();
        _playerId = Guid.NewGuid();

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
            PhoneNumberHash = "hash-close-test",
            TenantId = _tenantId
        });
        _dbContext.SaveChanges();
    }

    private Guid AddTable(int tableNumber, TableStatus status = TableStatus.Active, Guid? tenantId = null, Guid? gameCenterId = null)
    {
        var tableId = Guid.NewGuid();
        _dbContext.Tables.Add(new Table
        {
            Id = tableId,
            TenantId = tenantId ?? _tenantId,
            TableNumber = tableNumber,
            Status = status,
            QrCodeIdentifier = $"close-qr-{tableNumber}-{tableId}",
            GameCenterId = gameCenterId ?? _gameCenterId
        });
        return tableId;
    }

    private void AddSession(Guid tableId, GameSessionStatus status, string name = "Test Game")
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
        session.Teams.Add(new GameTeam
        {
            Id = Guid.NewGuid(),
            GameSessionId = session.Id,
            TeamName = "Team A",
            TeamNumber = 1
        });
        session.Teams.Add(new GameTeam
        {
            Id = Guid.NewGuid(),
            GameSessionId = session.Id,
            TeamName = "Team B",
            TeamNumber = 2
        });
        _dbContext.GameSessions.Add(session);
    }

    private async Task<(bool found, bool alreadyClosed, Table? table, List<GameSession> sessions)> InvokeCloseTable(Guid tableId)
    {
        var table = await _dbContext.Tables
            .Include(t => t.GameCenter)
            .FirstOrDefaultAsync(t => t.Id == tableId);

        if (table is null)
            return (false, false, null, new List<GameSession>());

        if (table.Status == TableStatus.Closed)
            return (true, true, table, new List<GameSession>());

        table.Status = TableStatus.Closed;
        table.UpdatedAt = DateTime.UtcNow;

        var sessionsToClose = await _dbContext.GameSessions
            .Where(gs => gs.TableId == tableId
                && (gs.Status == GameSessionStatus.Active
                    || gs.Status == GameSessionStatus.Approved
                    || gs.Status == GameSessionStatus.Pending))
            .ToListAsync();

        foreach (var session in sessionsToClose)
        {
            session.Status = GameSessionStatus.Closed;
            session.EndedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();

        return (true, false, table, sessionsToClose);
    }

    [Fact]
    public async Task CloseTable_SetsStatusToClosedAndCancelsActiveSessions()
    {
        _tenantProvider.SetTenantId(null);
        var tableId = AddTable(1);
        AddSession(tableId, GameSessionStatus.Active, "Active Game");
        await _dbContext.SaveChangesAsync();

        _tenantProvider.SetTenantId(_tenantId);
        var (found, alreadyClosed, table, closedSessions) = await InvokeCloseTable(tableId);

        Assert.True(found);
        Assert.False(alreadyClosed);
        Assert.Equal(TableStatus.Closed, table!.Status);
        Assert.Single(closedSessions);
        Assert.Equal(GameSessionStatus.Closed, closedSessions[0].Status);
        Assert.NotNull(closedSessions[0].EndedAt);
    }

    [Fact]
    public async Task CloseTable_Returns404ForNonExistentTable()
    {
        _tenantProvider.SetTenantId(_tenantId);
        var (found, _, _, _) = await InvokeCloseTable(Guid.NewGuid());

        Assert.False(found);
    }

    [Fact]
    public async Task CloseTable_Returns409ForAlreadyClosedTable()
    {
        _tenantProvider.SetTenantId(null);
        var tableId = AddTable(2, TableStatus.Closed);
        await _dbContext.SaveChangesAsync();

        _tenantProvider.SetTenantId(_tenantId);
        var (found, alreadyClosed, _, _) = await InvokeCloseTable(tableId);

        Assert.True(found);
        Assert.True(alreadyClosed);
    }

    [Fact]
    public async Task CloseTable_TenantIsolation_CannotCloseOtherTenantTable()
    {
        var otherTenantId = Guid.NewGuid();
        var otherGameCenterId = Guid.NewGuid();

        _tenantProvider.SetTenantId(null);
        _dbContext.GameCenters.Add(new GameCenter
        {
            Id = otherGameCenterId,
            Name = "Other Center",
            IsActive = true,
            MaxTables = 5
        });
        var otherTableId = AddTable(1, TableStatus.Active, otherTenantId, otherGameCenterId);
        await _dbContext.SaveChangesAsync();

        // Set tenant to our tenant — should NOT find the other tenant's table
        _tenantProvider.SetTenantId(_tenantId);
        var (found, _, _, _) = await InvokeCloseTable(otherTableId);

        Assert.False(found);
    }

    [Fact]
    public async Task CloseTable_CancelsPendingAndApprovedSessions()
    {
        _tenantProvider.SetTenantId(null);
        var tableId = AddTable(3);
        AddSession(tableId, GameSessionStatus.Pending, "Pending Game");
        AddSession(tableId, GameSessionStatus.Approved, "Approved Game");
        AddSession(tableId, GameSessionStatus.Completed, "Completed Game");
        AddSession(tableId, GameSessionStatus.Rejected, "Rejected Game");
        await _dbContext.SaveChangesAsync();

        _tenantProvider.SetTenantId(_tenantId);
        var (found, alreadyClosed, table, closedSessions) = await InvokeCloseTable(tableId);

        Assert.True(found);
        Assert.False(alreadyClosed);
        Assert.Equal(TableStatus.Closed, table!.Status);

        // Only Pending and Approved should be closed (not Completed or Rejected)
        Assert.Equal(2, closedSessions.Count);
        Assert.All(closedSessions, s => Assert.Equal(GameSessionStatus.Closed, s.Status));

        // Verify Completed and Rejected are unchanged
        _tenantProvider.SetTenantId(null);
        var completedSession = await _dbContext.GameSessions
            .FirstAsync(gs => gs.GameName == "Completed Game");
        var rejectedSession = await _dbContext.GameSessions
            .FirstAsync(gs => gs.GameName == "Rejected Game");

        Assert.Equal(GameSessionStatus.Completed, completedSession.Status);
        Assert.Equal(GameSessionStatus.Rejected, rejectedSession.Status);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
