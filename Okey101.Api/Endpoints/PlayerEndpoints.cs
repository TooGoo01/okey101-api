using Microsoft.EntityFrameworkCore;
using Okey101.Api.Data;
using Okey101.Api.Models.Enums;
using Okey101.Api.Models.Responses;
using Okey101.Api.Services;

namespace Okey101.Api.Endpoints;

public static class PlayerEndpoints
{
    public static RouteGroupBuilder MapPlayerEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/search", async (string? phone, AppDbContext db, IPhoneEncryptionService phoneEncryption) =>
        {
            if (string.IsNullOrWhiteSpace(phone))
            {
                return Results.BadRequest(new ApiErrorResponse(
                    "PHONE_REQUIRED",
                    "Phone number is required for search."));
            }

            var phoneHash = phoneEncryption.Hash(phone.Trim());

            var player = await db.Players
                .IgnoreQueryFilters()
                .Where(p => p.PhoneNumberHash == phoneHash)
                .Select(p => new { p.Id, p.Name })
                .FirstOrDefaultAsync();

            if (player is null)
            {
                return Results.Ok(new ApiResponse<object?>(null));
            }

            return Results.Ok(new ApiResponse<object>(new
            {
                playerId = player.Id,
                name = player.Name
            }));
        }).RequireAuthorization();

        group.MapGet("/{playerId:guid}/guest-matches", async (Guid playerId, AppDbContext db) =>
        {
            var player = await db.Players
                .IgnoreQueryFilters()
                .Where(p => p.Id == playerId)
                .Select(p => new { p.Name })
                .FirstOrDefaultAsync();

            if (player is null)
            {
                return Results.NotFound(new ApiErrorResponse(
                    "PLAYER_NOT_FOUND",
                    "Player not found."));
            }

            var matches = await db.Set<Models.Entities.GamePlayer>()
                .IgnoreQueryFilters()
                .Where(gp => gp.IsGuest && gp.PlayerId == null
                    && gp.GuestName != null
                    && gp.GuestName.ToLower() == player.Name.ToLowerInvariant())
                .Where(gp => gp.GameTeam.GameSession.Status == GameSessionStatus.Completed)
                .OrderByDescending(gp => gp.GameTeam.GameSession.CreatedAt)
                .Select(gp => new GuestMatchResponse
                {
                    GamePlayerId = gp.Id,
                    GuestName = gp.GuestName!,
                    GameName = gp.GameTeam.GameSession.GameName,
                    GameDate = gp.GameTeam.GameSession.CreatedAt,
                    GameCenterName = gp.GameTeam.GameSession.Table.GameCenter.Name,
                    SessionId = gp.GameTeam.GameSession.Id
                })
                .ToListAsync();

            return Results.Ok(new ApiListResponse<GuestMatchResponse>(matches, matches.Count));
        }).RequireAuthorization();

        group.MapPost("/{playerId:guid}/link-guest-history", async (Guid playerId, LinkGuestHistoryRequest request, AppDbContext db) =>
        {
            if (request.GamePlayerIds == null || request.GamePlayerIds.Count == 0)
            {
                return Results.BadRequest(new ApiErrorResponse(
                    "INVALID_REQUEST",
                    "At least one game player ID is required."));
            }

            var distinctIds = request.GamePlayerIds.Distinct().ToList();

            var player = await db.Players
                .IgnoreQueryFilters()
                .AnyAsync(p => p.Id == playerId);

            if (!player)
            {
                return Results.NotFound(new ApiErrorResponse(
                    "PLAYER_NOT_FOUND",
                    "Player not found."));
            }

            var gamePlayers = await db.Set<Models.Entities.GamePlayer>()
                .IgnoreQueryFilters()
                .Where(gp => distinctIds.Contains(gp.Id))
                .ToListAsync();

            if (gamePlayers.Count != distinctIds.Count)
            {
                return Results.BadRequest(new ApiErrorResponse(
                    "INVALID_GAME_PLAYER_IDS",
                    "One or more game player IDs are invalid."));
            }

            var invalidEntries = gamePlayers
                .Where(gp => !gp.IsGuest || gp.PlayerId != null)
                .ToList();

            if (invalidEntries.Any())
            {
                return Results.BadRequest(new ApiErrorResponse(
                    "INVALID_LINK_TARGET",
                    "One or more entries are not guest players or are already linked."));
            }

            foreach (var gp in gamePlayers)
            {
                gp.PlayerId = playerId;
            }

            await db.SaveChangesAsync();

            return Results.Ok(new ApiResponse<object>(new { linkedCount = gamePlayers.Count }));
        }).RequireAuthorization();

        return group;
    }
}

public class GuestMatchResponse
{
    public Guid GamePlayerId { get; set; }
    public string GuestName { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public DateTime GameDate { get; set; }
    public string? GameCenterName { get; set; }
    public Guid SessionId { get; set; }
}

public class LinkGuestHistoryRequest
{
    public List<Guid> GamePlayerIds { get; set; } = new();
}
