using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Okey101.Api.Data;
using Okey101.Api.Hubs;
using Okey101.Api.Models.Entities;
using Okey101.Api.Models.Enums;
using Okey101.Api.Models.Requests;
using Okey101.Api.Models.Responses;

namespace Okey101.Api.Endpoints;

public static class GameSessionEndpoints
{
    public static RouteGroupBuilder MapGameSessionEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/", async (CreateGameRequest request, AppDbContext db, IHubContext<GameHub> hubContext, HttpContext httpContext) =>
        {
            // Validate game name
            if (string.IsNullOrWhiteSpace(request.GameName))
            {
                return Results.BadRequest(new ApiErrorResponse(
                    "GAME_NAME_REQUIRED",
                    "Game name is required."));
            }

            if (request.GameName.Length > 100)
            {
                return Results.BadRequest(new ApiErrorResponse(
                    "GAME_NAME_TOO_LONG",
                    "Game name must be 100 characters or less."));
            }

            // Validate teams
            if (request.Teams.Count != 2)
            {
                return Results.BadRequest(new ApiErrorResponse(
                    "INVALID_TEAM_COUNT",
                    "Exactly two teams are required."));
            }

            if (request.Teams.Any(t => t.Players.Count == 0))
            {
                return Results.BadRequest(new ApiErrorResponse(
                    "TEAM_PLAYERS_REQUIRED",
                    "Each team must have at least one player."));
            }

            if (request.Teams.Any(t => t.Players.Count > 10))
            {
                return Results.BadRequest(new ApiErrorResponse(
                    "TOO_MANY_PLAYERS",
                    "Each team can have at most 10 players."));
            }

            // Validate table exists and is active
            var table = await db.Tables
                .IgnoreQueryFilters()
                .Include(t => t.GameCenter)
                .FirstOrDefaultAsync(t => t.Id == request.TableId);

            if (table is null)
            {
                return Results.NotFound(new ApiErrorResponse(
                    "TABLE_NOT_FOUND",
                    "The specified table was not found."));
            }

            if (table.Status != TableStatus.Active)
            {
                return Results.Conflict(new ApiErrorResponse(
                    "TABLE_NOT_ACTIVE",
                    "This table is not currently active."));
            }

            if (!table.GameCenter.IsActive)
            {
                return Results.Conflict(new ApiErrorResponse(
                    "GAME_CENTER_INACTIVE",
                    "This game center is not currently active."));
            }

            // Check no existing pending/active session on this table
            var existingSession = await db.GameSessions
                .IgnoreQueryFilters()
                .AnyAsync(gs => gs.TableId == request.TableId
                    && (gs.Status == GameSessionStatus.Pending || gs.Status == GameSessionStatus.Active || gs.Status == GameSessionStatus.Approved));

            if (existingSession)
            {
                return Results.Conflict(new ApiErrorResponse(
                    "TABLE_SESSION_EXISTS",
                    "This table already has an active or pending game session."));
            }

            // Extract current player from JWT claims
            var playerIdClaim = httpContext.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(playerIdClaim) || !Guid.TryParse(playerIdClaim, out var createdByPlayerId))
            {
                return Results.Unauthorized();
            }

            // Create game session
            var gameSession = new GameSession
            {
                Id = Guid.NewGuid(),
                TenantId = table.TenantId,
                TableId = request.TableId,
                GameName = request.GameName.Trim(),
                Status = GameSessionStatus.Pending,
                CreatedByPlayerId = createdByPlayerId,
                CreatedAt = DateTime.UtcNow
            };

            for (var i = 0; i < request.Teams.Count; i++)
            {
                var teamRequest = request.Teams[i];
                var gameTeam = new GameTeam
                {
                    Id = Guid.NewGuid(),
                    GameSessionId = gameSession.Id,
                    TeamName = string.IsNullOrWhiteSpace(teamRequest.TeamName)
                        ? $"Team {i + 1}"
                        : teamRequest.TeamName.Trim(),
                    TeamNumber = i + 1
                };

                foreach (var playerRequest in teamRequest.Players)
                {
                    var gamePlayer = new GamePlayer
                    {
                        Id = Guid.NewGuid(),
                        GameTeamId = gameTeam.Id,
                        IsGuest = playerRequest.IsGuest
                    };

                    if (playerRequest.IsGuest)
                    {
                        if (string.IsNullOrWhiteSpace(playerRequest.GuestName))
                        {
                            return Results.BadRequest(new ApiErrorResponse(
                                "GUEST_NAME_REQUIRED",
                                "Guest player name is required."));
                        }
                        if (playerRequest.GuestName.Trim().Length > 100)
                        {
                            return Results.BadRequest(new ApiErrorResponse(
                                "GUEST_NAME_TOO_LONG",
                                "Guest player name must be 100 characters or less."));
                        }
                        gamePlayer.GuestName = playerRequest.GuestName.Trim();
                    }
                    else
                    {
                        if (!playerRequest.PlayerId.HasValue)
                        {
                            return Results.BadRequest(new ApiErrorResponse(
                                "PLAYER_ID_REQUIRED",
                                "Registered player ID is required."));
                        }
                        gamePlayer.PlayerId = playerRequest.PlayerId.Value;
                    }

                    gameTeam.Players.Add(gamePlayer);
                }

                gameSession.Teams.Add(gameTeam);
            }

            // Auto-add creating player to Team 1 if not already present
            var team1 = gameSession.Teams.First(t => t.TeamNumber == 1);
            if (!gameSession.Teams.SelectMany(t => t.Players).Any(p => p.PlayerId == createdByPlayerId))
            {
                team1.Players.Add(new GamePlayer
                {
                    Id = Guid.NewGuid(),
                    GameTeamId = team1.Id,
                    PlayerId = createdByPlayerId,
                    IsGuest = false
                });
            }

            // Validate no duplicate registered player IDs across teams
            var registeredPlayerIds = gameSession.Teams
                .SelectMany(t => t.Players)
                .Where(p => !p.IsGuest && p.PlayerId.HasValue)
                .Select(p => p.PlayerId!.Value)
                .ToList();

            if (registeredPlayerIds.Count != registeredPlayerIds.Distinct().Count())
            {
                return Results.BadRequest(new ApiErrorResponse(
                    "DUPLICATE_PLAYER",
                    "A player cannot be added to multiple teams or added twice."));
            }

            // Validate all registered player IDs exist in the database
            var distinctPlayerIds = registeredPlayerIds.Distinct().ToList();
            if (distinctPlayerIds.Count > 0)
            {
                var existingCount = await db.Players
                    .IgnoreQueryFilters()
                    .CountAsync(p => distinctPlayerIds.Contains(p.Id));

                if (existingCount != distinctPlayerIds.Count)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        "PLAYER_NOT_FOUND",
                        "One or more registered player IDs are invalid."));
                }
            }

            db.GameSessions.Add(gameSession);
            await db.SaveChangesAsync();

            // Load registered player names for response
            var playerNameMap = distinctPlayerIds.Count > 0
                ? await db.Players
                    .IgnoreQueryFilters()
                    .Where(p => distinctPlayerIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, p => p.Name)
                : new Dictionary<Guid, string>();

            // Build response
            var response = new GameSessionResponse
            {
                SessionId = gameSession.Id,
                GameName = gameSession.GameName,
                Status = gameSession.Status.ToString(),
                CreatedAt = gameSession.CreatedAt,
                TableNumber = table.TableNumber,
                GameCenterName = table.GameCenter.Name,
                Teams = gameSession.Teams.Select(t => new GameTeamResponse
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

            // Broadcast admin notification (fire-and-forget — don't fail creation if notification fails)
            try
            {
                var notification = new NewGameNotification
                {
                    SessionId = gameSession.Id,
                    GameName = gameSession.GameName,
                    TableNumber = table.TableNumber,
                    GameCenterName = table.GameCenter.Name,
                    PlayerCount = gameSession.Teams.SelectMany(t => t.Players).Count(),
                    CreatedAt = gameSession.CreatedAt
                };
                await hubContext.Clients.Group($"gamecenter_{table.GameCenterId}").SendAsync("NewGameRequest", notification);
            }
            catch
            {
                // Notification failure should not affect game creation
            }

            return Results.Created($"/api/v1/game-sessions/{gameSession.Id}", new ApiResponse<GameSessionResponse>(response));
        }).RequireAuthorization();

        // GET /active — admin-only: list active + approved sessions for the admin's tenant
        group.MapGet("/active", async (AppDbContext db) =>
        {
            // Tenant filter applied automatically via TenantMiddleware — no IgnoreQueryFilters
            var sessions = await db.GameSessions
                .Include(gs => gs.Table)
                    .ThenInclude(t => t.GameCenter)
                .Include(gs => gs.Teams)
                    .ThenInclude(t => t.Players)
                .Where(gs => gs.Status == GameSessionStatus.Active || gs.Status == GameSessionStatus.Approved)
                .OrderByDescending(gs => gs.CreatedAt)
                .ToListAsync();

            // Batch-load current round for each session from ScoreEntries
            var sessionIds = sessions.Select(s => s.Id).ToList();
            var currentRounds = sessionIds.Count > 0
                ? await db.ScoreEntries
                    .Where(e => sessionIds.Contains(e.GameSessionId) && !e.IsRemoved)
                    .GroupBy(e => e.GameSessionId)
                    .Select(g => new { SessionId = g.Key, MaxRound = g.Max(e => e.RoundNumber) })
                    .ToDictionaryAsync(x => x.SessionId, x => x.MaxRound)
                : new Dictionary<Guid, int>();

            // Load player names for registered players
            var allPlayerIds = sessions
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

            var responseList = sessions.Select(gs =>
            {
                // Default to round 1 when no score entries exist yet
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

            return Results.Ok(new ApiListResponse<AdminGameSessionResponse>(responseList, responseList.Count));
        }).RequireAuthorization("AdminOnly");

        // GET /pending — admin-only: list pending sessions for the admin's tenant
        group.MapGet("/pending", async (AppDbContext db) =>
        {
            // Tenant filter applied automatically via TenantMiddleware — no IgnoreQueryFilters
            var sessions = await db.GameSessions
                .Include(gs => gs.Table)
                    .ThenInclude(t => t.GameCenter)
                .Include(gs => gs.CreatedByPlayer)
                .Include(gs => gs.Teams)
                    .ThenInclude(t => t.Players)
                .Where(gs => gs.Status == GameSessionStatus.Pending)
                .OrderByDescending(gs => gs.CreatedAt)
                .ToListAsync();

            // Load player names for registered players
            var allPlayerIds = sessions
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

            var responseList = sessions.Select(gs => new GameSessionResponse
            {
                SessionId = gs.Id,
                GameName = gs.GameName,
                Status = gs.Status.ToString(),
                CreatedAt = gs.CreatedAt,
                ApprovedAt = gs.ApprovedAt,
                TableNumber = gs.Table.TableNumber,
                GameCenterName = gs.Table.GameCenter.Name,
                CreatedByPlayerName = gs.CreatedByPlayer?.Name,
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
            }).ToList();

            return Results.Ok(new ApiListResponse<GameSessionResponse>(responseList, responseList.Count));
        }).RequireAuthorization("AdminOnly");

        // GET / — list game sessions with optional filters
        group.MapGet("/", async (AppDbContext db, HttpContext httpContext, string? status, Guid? gameCenterId, Guid? playerId, int? skip, int? take) =>
        {
            var query = db.GameSessions
                .IgnoreQueryFilters()
                .Include(gs => gs.Table)
                    .ThenInclude(t => t.GameCenter)
                .Include(gs => gs.Teams)
                    .ThenInclude(t => t.Players)
                .AsQueryable();

            if (playerId.HasValue)
            {
                query = query.Where(gs => gs.Teams.Any(t => t.Players.Any(p =>
                    (p.PlayerId.HasValue && p.PlayerId.Value == playerId.Value) ||
                    (!p.PlayerId.HasValue && p.Id == playerId.Value))));
                // When filtering by player, only show completed games (history)
                query = query.Where(gs => gs.Status == GameSessionStatus.Completed);
            }
            else if (!string.IsNullOrEmpty(status) && Enum.TryParse<GameSessionStatus>(status, true, out var statusEnum))
            {
                query = query.Where(gs => gs.Status == statusEnum);
            }

            if (gameCenterId.HasValue)
            {
                query = query.Where(gs => gs.Table.GameCenterId == gameCenterId.Value);
            }

            var orderedQuery = query.OrderByDescending(gs => gs.CreatedAt);

            var totalCount = await orderedQuery.CountAsync();

            var paginatedQuery = orderedQuery.AsQueryable();
            if (skip.HasValue && skip.Value > 0)
            {
                paginatedQuery = paginatedQuery.Skip(skip.Value);
            }
            if (take.HasValue && take.Value > 0)
            {
                paginatedQuery = paginatedQuery.Take(take.Value);
            }

            var sessions = await paginatedQuery.ToListAsync();

            // Load player names for registered players
            var allPlayerIds = sessions
                .SelectMany(gs => gs.Teams)
                .SelectMany(t => t.Players)
                .Where(p => !p.IsGuest && p.PlayerId.HasValue)
                .Select(p => p.PlayerId!.Value)
                .Distinct()
                .ToList();

            var playerNameMap = allPlayerIds.Count > 0
                ? await db.Players
                    .IgnoreQueryFilters()
                    .Where(p => allPlayerIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, p => p.Name)
                : new Dictionary<Guid, string>();

            var responseList = sessions.Select(gs => new GameSessionResponse
            {
                SessionId = gs.Id,
                GameName = gs.GameName,
                Status = gs.Status.ToString(),
                CreatedAt = gs.CreatedAt,
                ApprovedAt = gs.ApprovedAt,
                EndedAt = gs.EndedAt,
                TableNumber = gs.Table.TableNumber,
                GameCenterName = gs.Table.GameCenter.Name,
                WinnerTeamNumber = gs.WinnerTeamNumber,
                Team1FinalTotal = gs.Team1FinalTotal,
                Team2FinalTotal = gs.Team2FinalTotal,
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
            }).ToList();

            return Results.Ok(new ApiListResponse<GameSessionResponse>(responseList, totalCount));
        }).RequireAuthorization();

        // POST /{id}/approve — approve a pending game session
        group.MapPost("/{id}/approve", async (Guid id, AppDbContext db, IHubContext<GameHub> hubContext) =>
        {
            var session = await db.GameSessions
                .IgnoreQueryFilters()
                .Include(gs => gs.Table)
                    .ThenInclude(t => t.GameCenter)
                .Include(gs => gs.Teams)
                    .ThenInclude(t => t.Players)
                .FirstOrDefaultAsync(gs => gs.Id == id);

            if (session is null)
            {
                return Results.NotFound(new ApiErrorResponse(
                    "SESSION_NOT_FOUND",
                    "The specified game session was not found."));
            }

            if (session.Status != GameSessionStatus.Pending)
            {
                return Results.Conflict(new ApiErrorResponse(
                    "SESSION_NOT_PENDING",
                    "Only pending game sessions can be approved."));
            }

            session.Status = GameSessionStatus.Active;
            session.ApprovedAt = DateTime.UtcNow;
            session.StartedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            // Load player names for response
            var playerIds = session.Teams
                .SelectMany(t => t.Players)
                .Where(p => !p.IsGuest && p.PlayerId.HasValue)
                .Select(p => p.PlayerId!.Value)
                .Distinct()
                .ToList();

            var playerNameMap = playerIds.Count > 0
                ? await db.Players
                    .IgnoreQueryFilters()
                    .Where(p => playerIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, p => p.Name)
                : new Dictionary<Guid, string>();

            var response = new GameSessionResponse
            {
                SessionId = session.Id,
                GameName = session.GameName,
                Status = session.Status.ToString(),
                CreatedAt = session.CreatedAt,
                ApprovedAt = session.ApprovedAt,
                TableNumber = session.Table.TableNumber,
                GameCenterName = session.Table.GameCenter.Name,
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

            // Broadcast approval to session group and admin group (fire-and-forget)
            try
            {
                var approvalPayload = new
                {
                    sessionId = session.Id,
                    gameName = session.GameName,
                    approvedAt = session.ApprovedAt
                };
                await hubContext.Clients.Group($"session_{id}").SendAsync("GameApproved", approvalPayload);
                await hubContext.Clients.Group($"gamecenter_{session.Table.GameCenterId}").SendAsync("GameApproved", approvalPayload);
            }
            catch
            {
                // Notification failure should not affect approval
            }

            return Results.Ok(new ApiResponse<GameSessionResponse>(response));
        }).RequireAuthorization();

        // POST /{id}/reject — reject a pending game session
        group.MapPost("/{id}/reject", async (Guid id, AppDbContext db, IHubContext<GameHub> hubContext) =>
        {
            var session = await db.GameSessions
                .IgnoreQueryFilters()
                .Include(gs => gs.Table)
                    .ThenInclude(t => t.GameCenter)
                .Include(gs => gs.Teams)
                    .ThenInclude(t => t.Players)
                .FirstOrDefaultAsync(gs => gs.Id == id);

            if (session is null)
            {
                return Results.NotFound(new ApiErrorResponse(
                    "SESSION_NOT_FOUND",
                    "The specified game session was not found."));
            }

            if (session.Status != GameSessionStatus.Pending)
            {
                return Results.Conflict(new ApiErrorResponse(
                    "SESSION_NOT_PENDING",
                    "Only pending game sessions can be rejected."));
            }

            session.Status = GameSessionStatus.Rejected;
            session.EndedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            // Load player names for response
            var playerIds = session.Teams
                .SelectMany(t => t.Players)
                .Where(p => !p.IsGuest && p.PlayerId.HasValue)
                .Select(p => p.PlayerId!.Value)
                .Distinct()
                .ToList();

            var playerNameMap = playerIds.Count > 0
                ? await db.Players
                    .IgnoreQueryFilters()
                    .Where(p => playerIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, p => p.Name)
                : new Dictionary<Guid, string>();

            var response = new GameSessionResponse
            {
                SessionId = session.Id,
                GameName = session.GameName,
                Status = session.Status.ToString(),
                CreatedAt = session.CreatedAt,
                TableNumber = session.Table.TableNumber,
                GameCenterName = session.Table.GameCenter.Name,
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

            // Broadcast rejection to session group and admin group (fire-and-forget)
            try
            {
                var rejectionPayload = new
                {
                    sessionId = session.Id,
                    gameName = session.GameName,
                    rejectedAt = session.EndedAt
                };
                await hubContext.Clients.Group($"session_{id}").SendAsync("GameRejected", rejectionPayload);
                await hubContext.Clients.Group($"gamecenter_{session.Table.GameCenterId}").SendAsync("GameRejected", rejectionPayload);
            }
            catch
            {
                // Notification failure should not affect rejection
            }

            return Results.Ok(new ApiResponse<GameSessionResponse>(response));
        }).RequireAuthorization();

        // POST /{id}/complete — mark a game session as completed after round 8
        group.MapPost("/{id}/complete", async (Guid id, AppDbContext db, IHubContext<GameHub> hubContext) =>
        {
            var session = await db.GameSessions
                .IgnoreQueryFilters()
                .Include(gs => gs.Teams)
                .FirstOrDefaultAsync(gs => gs.Id == id);

            if (session is null)
            {
                return Results.NotFound(new ApiErrorResponse(
                    "SESSION_NOT_FOUND",
                    "The specified game session was not found."));
            }

            if (session.Status != GameSessionStatus.Active)
            {
                return Results.BadRequest(new ApiErrorResponse(
                    "SESSION_NOT_ACTIVE",
                    "Only active game sessions can be completed."));
            }

            // Verify both teams have at least one endOfRound entry in round 8
            var round8Entries = await db.ScoreEntries
                .IgnoreQueryFilters()
                .Where(e => e.GameSessionId == id && e.RoundNumber == 8
                    && e.ScoreType == Models.Enums.ScoreType.EndOfRound && !e.IsRemoved)
                .ToListAsync();

            var team1HasEndOfRound = round8Entries.Any(e => e.TeamNumber == 1);
            var team2HasEndOfRound = round8Entries.Any(e => e.TeamNumber == 2);

            if (!team1HasEndOfRound || !team2HasEndOfRound)
            {
                return Results.BadRequest(new ApiErrorResponse(
                    "ROUND_8_INCOMPLETE",
                    "Both teams must have end-of-round scores in round 8 to complete the game."));
            }

            // Calculate final totals
            var allEntries = await db.ScoreEntries
                .IgnoreQueryFilters()
                .Where(e => e.GameSessionId == id && !e.IsRemoved)
                .ToListAsync();

            var team1Total = allEntries.Where(e => e.TeamNumber == 1).Sum(e => e.Value);
            var team2Total = allEntries.Where(e => e.TeamNumber == 2).Sum(e => e.Value);

            var team1 = session.Teams.FirstOrDefault(t => t.TeamNumber == 1);
            var team2 = session.Teams.FirstOrDefault(t => t.TeamNumber == 2);

            int? winner = null;
            if (team1Total > team2Total) winner = 1;
            else if (team2Total > team1Total) winner = 2;

            // Mark session as completed and persist winner/totals
            session.Status = GameSessionStatus.Completed;
            session.EndedAt = DateTime.UtcNow;
            session.WinnerTeamNumber = winner;
            session.Team1FinalTotal = team1Total;
            session.Team2FinalTotal = team2Total;
            await db.SaveChangesAsync();

            var response = new GameCompletionResponse
            {
                SessionId = session.Id,
                Team1Name = team1?.TeamName ?? "Team 1",
                Team2Name = team2?.TeamName ?? "Team 2",
                Team1Total = team1Total,
                Team2Total = team2Total,
                Winner = winner,
                CompletedAt = session.EndedAt!.Value,
            };

            // Broadcast GameCompleted to all clients in the session group (fire-and-forget)
            try
            {
                await hubContext.Clients.Group($"session_{id}").SendAsync("GameCompleted", response);
            }
            catch
            {
                // Broadcast failure should not affect completion
            }

            return Results.Ok(new ApiResponse<GameCompletionResponse>(response));
        }).RequireAuthorization();

        // POST /{id}/rematch — start a new game with the same teams
        group.MapPost("/{id}/rematch", async (Guid id, AppDbContext db, IHubContext<GameHub> hubContext) =>
        {
            var session = await db.GameSessions
                .IgnoreQueryFilters()
                .Include(gs => gs.Table)
                .Include(gs => gs.Teams)
                    .ThenInclude(t => t.Players)
                .FirstOrDefaultAsync(gs => gs.Id == id);

            if (session is null)
            {
                return Results.NotFound(new ApiErrorResponse(
                    "SESSION_NOT_FOUND",
                    "The specified game session was not found."));
            }

            if (session.Status != GameSessionStatus.Completed)
            {
                return Results.BadRequest(new ApiErrorResponse(
                    "SESSION_NOT_COMPLETED",
                    "Only completed game sessions can be rematched."));
            }

            if (session.Table.Status != TableStatus.Active)
            {
                return Results.BadRequest(new ApiErrorResponse(
                    "TABLE_CLOSED",
                    "This table has been closed. Scan a QR code to start a new game."));
            }

            if (session.CreatedAt.Date != DateTime.UtcNow.Date)
            {
                return Results.BadRequest(new ApiErrorResponse(
                    "NOT_SAME_DAY",
                    "A new day has started. Scan a QR code to play again."));
            }

            // Check no existing pending/active/approved session on this table
            var existingSession = await db.GameSessions
                .IgnoreQueryFilters()
                .AnyAsync(gs => gs.TableId == session.TableId
                    && gs.Id != id
                    && (gs.Status == GameSessionStatus.Pending || gs.Status == GameSessionStatus.Active || gs.Status == GameSessionStatus.Approved));

            if (existingSession)
            {
                return Results.Conflict(new ApiErrorResponse(
                    "TABLE_SESSION_EXISTS",
                    "This table already has an active or pending game session."));
            }

            // Auto-generate game name
            var gameName = $"{session.GameName} - Rematch";
            if (gameName.Length > 100)
            {
                gameName = gameName[..100];
            }

            // Create new session as Active (skip Pending/Approved)
            var newSession = new GameSession
            {
                Id = Guid.NewGuid(),
                TenantId = session.TenantId,
                TableId = session.TableId,
                GameName = gameName,
                Status = GameSessionStatus.Active,
                CreatedByPlayerId = session.CreatedByPlayerId,
                CreatedAt = DateTime.UtcNow,
                StartedAt = DateTime.UtcNow,
            };

            // Copy teams and players from parent session
            foreach (var parentTeam in session.Teams)
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

            db.GameSessions.Add(newSession);
            await db.SaveChangesAsync();

            var team1 = newSession.Teams.FirstOrDefault(t => t.TeamNumber == 1);
            var team2 = newSession.Teams.FirstOrDefault(t => t.TeamNumber == 2);

            var response = new RematchResponse
            {
                SessionId = newSession.Id,
                GameName = newSession.GameName,
                Team1Name = team1?.TeamName ?? "Team 1",
                Team2Name = team2?.TeamName ?? "Team 2",
            };

            // Broadcast RematchStarted to all clients in the parent session group (fire-and-forget)
            try
            {
                await hubContext.Clients.Group($"session_{id}").SendAsync("RematchStarted", response);
            }
            catch
            {
                // Broadcast failure should not affect rematch creation
            }

            return Results.Ok(new ApiResponse<RematchResponse>(response));
        }).RequireAuthorization();

        // GET /api/v1/game-sessions/{id}/result — retrieve persisted game result
        group.MapGet("/{id:guid}/result", async (
            Guid id,
            AppDbContext db) =>
        {
            var session = await db.GameSessions
                .IgnoreQueryFilters()
                .Include(gs => gs.Teams)
                .FirstOrDefaultAsync(gs => gs.Id == id);

            if (session is null)
            {
                return Results.NotFound(new ApiErrorResponse(
                    "SESSION_NOT_FOUND",
                    "The specified game session was not found."));
            }

            if (session.Status != GameSessionStatus.Completed)
            {
                return Results.BadRequest(new ApiErrorResponse(
                    "SESSION_NOT_COMPLETED",
                    "Game result is only available for completed sessions."));
            }

            var team1 = session.Teams.FirstOrDefault(t => t.TeamNumber == 1);
            var team2 = session.Teams.FirstOrDefault(t => t.TeamNumber == 2);

            var response = new GameCompletionResponse
            {
                SessionId = session.Id,
                Team1Name = team1?.TeamName ?? "Team 1",
                Team2Name = team2?.TeamName ?? "Team 2",
                Team1Total = session.Team1FinalTotal,
                Team2Total = session.Team2FinalTotal,
                Winner = session.WinnerTeamNumber,
                CompletedAt = session.EndedAt ?? DateTime.UtcNow,
            };

            return Results.Ok(new ApiResponse<GameCompletionResponse>(response));
        }).RequireAuthorization();

        return group;
    }
}
