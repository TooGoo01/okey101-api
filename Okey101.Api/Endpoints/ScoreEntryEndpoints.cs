using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Okey101.Api.Data;
using Okey101.Api.Hubs;
using Okey101.Api.Models.Entities;
using Okey101.Api.Models.Requests;
using Okey101.Api.Models.Responses;

namespace Okey101.Api.Endpoints;

public static class ScoreEntryEndpoints
{
    public static RouteGroupBuilder MapScoreEntryEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/{sessionId:guid}/score-entries", async (
            Guid sessionId,
            AppDbContext db) =>
        {
            var session = await db.GameSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sessionId);

            if (session == null)
            {
                return Results.NotFound(new ApiErrorResponse("SESSION_NOT_FOUND", "Game session not found."));
            }

            var entries = await db.ScoreEntries
                .AsNoTracking()
                .Where(e => e.GameSessionId == sessionId)
                .OrderBy(e => e.RoundNumber)
                .ThenBy(e => e.CreatedAt)
                .Select(e => new ScoreEntryResponse
                {
                    Id = e.Id,
                    GameSessionId = e.GameSessionId,
                    TeamNumber = e.TeamNumber,
                    RoundNumber = e.RoundNumber,
                    ScoreType = e.ScoreType == Models.Enums.ScoreType.Fine ? "fine" : "endOfRound",
                    Value = e.Value,
                    CreatedByPlayerId = e.CreatedByPlayerId,
                    CreatedAt = e.CreatedAt,
                    IsRemoved = e.IsRemoved,
                    RemovedAt = e.RemovedAt,
                })
                .ToListAsync();

            return Results.Ok(new ApiListResponse<ScoreEntryResponse>(entries, entries.Count));
        }).RequireAuthorization();

        group.MapPost("/{sessionId:guid}/score-entries", async (
            Guid sessionId,
            CreateScoreEntryRequest request,
            AppDbContext db,
            IHubContext<GameHub> hubContext) =>
        {
            var session = await db.GameSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sessionId);

            if (session == null)
            {
                return Results.NotFound(new ApiErrorResponse("SESSION_NOT_FOUND", "Game session not found."));
            }

            if (session.Status != Models.Enums.GameSessionStatus.Active)
            {
                return Results.BadRequest(new ApiErrorResponse("SESSION_NOT_ACTIVE", "Scores can only be added to active game sessions."));
            }

            // Validate request fields
            var errors = new List<ApiErrorDetail>();

            if (request.TeamNumber is not (1 or 2))
            {
                errors.Add(new ApiErrorDetail { Field = "teamNumber", Issue = "Must be 1 or 2" });
            }

            if (request.RoundNumber is < 1 or > 8)
            {
                errors.Add(new ApiErrorDetail { Field = "roundNumber", Issue = "Must be between 1 and 8" });
            }

            if (request.ScoreType is not ("fine" or "endOfRound"))
            {
                errors.Add(new ApiErrorDetail { Field = "scoreType", Issue = "Must be 'fine' or 'endOfRound'" });
            }

            // Fine-specific validation
            if (request.ScoreType == "fine")
            {
                if (request.Value <= 0 || request.Value > 200 || request.Value % 10 != 0)
                {
                    errors.Add(new ApiErrorDetail { Field = "value", Issue = "Must be 10, 20, 30... up to 200" });
                }
            }

            // End-of-round validation
            if (request.ScoreType == "endOfRound")
            {
                if (request.Value == 0)
                {
                    errors.Add(new ApiErrorDetail { Field = "value", Issue = "Score value cannot be zero" });
                }
            }

            if (errors.Count > 0)
            {
                return Results.BadRequest(new ApiErrorResponse("VALIDATION_ERROR", "Invalid score entry.", errors));
            }

            var scoreType = request.ScoreType == "fine"
                ? Models.Enums.ScoreType.Fine
                : Models.Enums.ScoreType.EndOfRound;

            // Hardcode test player ID for now — real auth deferred
            var playerId = session.CreatedByPlayerId;

            var entry = new ScoreEntry
            {
                Id = Guid.NewGuid(),
                GameSessionId = sessionId,
                TeamNumber = request.TeamNumber,
                RoundNumber = request.RoundNumber,
                ScoreType = scoreType,
                Value = request.Value,
                CreatedByPlayerId = playerId,
                CreatedAt = DateTime.UtcNow,
                IsRemoved = false,
            };

            db.ScoreEntries.Add(entry);
            await db.SaveChangesAsync();

            var response = new ScoreEntryResponse
            {
                Id = entry.Id,
                GameSessionId = entry.GameSessionId,
                TeamNumber = entry.TeamNumber,
                RoundNumber = entry.RoundNumber,
                ScoreType = request.ScoreType,
                Value = entry.Value,
                CreatedByPlayerId = entry.CreatedByPlayerId,
                CreatedAt = entry.CreatedAt,
                IsRemoved = entry.IsRemoved,
                RemovedAt = entry.RemovedAt,
            };

            // Broadcast to all clients in the session group (fire-and-forget)
            try
            {
                await hubContext.Clients.Group($"session_{sessionId}").SendAsync("ScoreEntryAdded", response);
            }
            catch
            {
                // Broadcast failure must not fail the HTTP response
            }

            return Results.Created(
                $"/api/v1/game-sessions/{sessionId}/score-entries",
                new ApiResponse<ScoreEntryResponse>(response));
        }).RequireAuthorization();

        group.MapPatch("/{sessionId:guid}/score-entries/{entryId:guid}/remove", async (
            Guid sessionId,
            Guid entryId,
            AppDbContext db,
            IHubContext<GameHub> hubContext) =>
        {
            var session = await db.GameSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sessionId);

            if (session == null)
            {
                return Results.NotFound(new ApiErrorResponse("SESSION_NOT_FOUND", "Game session not found."));
            }

            if (session.Status != Models.Enums.GameSessionStatus.Active)
            {
                return Results.BadRequest(new ApiErrorResponse("SESSION_NOT_ACTIVE", "Scores can only be modified in active game sessions."));
            }

            var entry = await db.ScoreEntries
                .FirstOrDefaultAsync(e => e.Id == entryId && e.GameSessionId == sessionId);

            if (entry == null)
            {
                return Results.NotFound(new ApiErrorResponse("ENTRY_NOT_FOUND", "Score entry not found."));
            }

            if (entry.IsRemoved)
            {
                return Results.BadRequest(new ApiErrorResponse("ALREADY_REMOVED", "Score entry is already removed."));
            }

            entry.IsRemoved = true;
            entry.RemovedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            var response = new ScoreEntryResponse
            {
                Id = entry.Id,
                GameSessionId = entry.GameSessionId,
                TeamNumber = entry.TeamNumber,
                RoundNumber = entry.RoundNumber,
                ScoreType = entry.ScoreType == Models.Enums.ScoreType.Fine ? "fine" : "endOfRound",
                Value = entry.Value,
                CreatedByPlayerId = entry.CreatedByPlayerId,
                CreatedAt = entry.CreatedAt,
                IsRemoved = entry.IsRemoved,
                RemovedAt = entry.RemovedAt,
            };

            try
            {
                await hubContext.Clients.Group($"session_{sessionId}").SendAsync("ScoreEntryRemoved", response);
            }
            catch
            {
                // Broadcast failure must not fail the HTTP response
            }

            return Results.Ok(new ApiResponse<ScoreEntryResponse>(response));
        }).RequireAuthorization();

        return group;
    }
}
