using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Okey101.Api.Data;
using Okey101.Api.Hubs;
using Okey101.Api.Models.Enums;
using Okey101.Api.Models.Responses;

namespace Okey101.Api.Endpoints;

public static class TableEndpoints
{
    public static RouteGroupBuilder MapTableEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/resolve", async (string? qr, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(qr))
            {
                return Results.BadRequest(new ApiErrorResponse(
                    "INVALID_REQUEST",
                    "QR code identifier is required."));
            }

            var table = await db.Tables
                .IgnoreQueryFilters()
                .Include(t => t.GameCenter)
                .FirstOrDefaultAsync(t => t.QrCodeIdentifier == qr);

            if (table is null)
            {
                return Results.NotFound(new ApiErrorResponse(
                    "TABLE_NOT_FOUND",
                    "QR code is not valid."));
            }

            if (!table.GameCenter.IsActive)
            {
                return Results.Conflict(new ApiErrorResponse(
                    "GAME_CENTER_INACTIVE",
                    "This game center is not currently active."));
            }

            if (table.Status != TableStatus.Active)
            {
                return Results.Conflict(new ApiErrorResponse(
                    "TABLE_UNAVAILABLE",
                    "This table is not available."));
            }

            return Results.Ok(new ApiResponse<TableResolveResponse>(new TableResolveResponse
            {
                TableId = table.Id,
                TableNumber = table.TableNumber,
                GameCenterId = table.GameCenterId,
                GameCenterName = table.GameCenter.Name
            }));
        }).RequireAuthorization();

        // GET / — admin-only: list all tables with derived display status from current game sessions
        group.MapGet("/", async (AppDbContext db) =>
        {
            // Tenant filter applied automatically via TenantMiddleware — no IgnoreQueryFilters
            var tables = await db.Tables
                .Where(t => t.Status == TableStatus.Active)
                .OrderBy(t => t.TableNumber)
                .ToListAsync();

            var tableIds = tables.Select(t => t.Id).ToList();

            // For each table, find the latest non-completed/closed/rejected game session
            // Priority: Active > Approved > Pending
            var activeSessions = tableIds.Count > 0
                ? await db.GameSessions
                    .Include(gs => gs.Teams)
                        .ThenInclude(t => t.Players)
                    .Where(gs => tableIds.Contains(gs.TableId)
                        && (gs.Status == GameSessionStatus.Active
                            || gs.Status == GameSessionStatus.Approved
                            || gs.Status == GameSessionStatus.Pending))
                    .ToListAsync()
                : new List<Models.Entities.GameSession>();

            // Group by table and pick highest priority session per table
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

            // Batch-load player names for registered players
            var allPlayerIds = activeSessions
                .SelectMany(gs => gs.Teams)
                .SelectMany(t => t.Players)
                .Where(p => !p.IsGuest && p.PlayerId.HasValue)
                .Select(p => p.PlayerId!.Value)
                .Distinct()
                .ToList();

            var playerNameMap = allPlayerIds.Count > 0
                ? await db.Players
                    .Where(p => allPlayerIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, p => p.Name)
                : new Dictionary<Guid, string>();

            var responseList = tables.Select(table =>
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

            return Results.Ok(new ApiListResponse<AdminTableResponse>(responseList, responseList.Count));
        }).RequireAuthorization("AdminOnly");

        // POST /{id}/close — admin-only: close a table and cancel all active sessions
        group.MapPost("/{id}/close", async (Guid id, AppDbContext db, IHubContext<GameHub> hubContext) =>
        {
            // Tenant filter applied automatically via TenantMiddleware — no IgnoreQueryFilters
            var table = await db.Tables
                .Include(t => t.GameCenter)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (table is null)
            {
                return Results.NotFound(new ApiErrorResponse(
                    "TABLE_NOT_FOUND",
                    "The specified table was not found."));
            }

            if (table.Status == TableStatus.Closed)
            {
                return Results.Conflict(new ApiErrorResponse(
                    "TABLE_ALREADY_CLOSED",
                    "This table has already been closed."));
            }

            // Close the table
            table.Status = TableStatus.Closed;
            table.UpdatedAt = DateTime.UtcNow;

            // Find and close all active/approved/pending game sessions on this table
            var sessionsToClose = await db.GameSessions
                .Where(gs => gs.TableId == id
                    && (gs.Status == GameSessionStatus.Active
                        || gs.Status == GameSessionStatus.Approved
                        || gs.Status == GameSessionStatus.Pending))
                .ToListAsync();

            foreach (var session in sessionsToClose)
            {
                session.Status = GameSessionStatus.Closed;
                session.EndedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync();

            // Broadcast TableClosed to each affected session group and the game center group (fire-and-forget)
            try
            {
                var payload = new
                {
                    tableId = table.Id,
                    tableNumber = table.TableNumber
                };

                foreach (var session in sessionsToClose)
                {
                    await hubContext.Clients.Group($"session_{session.Id}").SendAsync("TableClosed", payload);
                }
                await hubContext.Clients.Group($"gamecenter_{table.GameCenterId}").SendAsync("TableClosed", payload);
            }
            catch
            {
                // Notification failure should not affect table closure
            }

            return Results.Ok(new ApiResponse<object>(new
            {
                message = "Table closed successfully.",
                cancelledSessionCount = sessionsToClose.Count
            }));
        }).RequireAuthorization("AdminOnly");

        return group;
    }
}
