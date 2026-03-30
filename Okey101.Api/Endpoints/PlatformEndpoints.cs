using Microsoft.EntityFrameworkCore;
using Okey101.Api.Data;
using Okey101.Api.Models.Entities;
using Okey101.Api.Models.Enums;
using Okey101.Api.Models.Responses;

namespace Okey101.Api.Endpoints;

public static class PlatformEndpoints
{
    public static RouteGroupBuilder MapPlatformEndpoints(this RouteGroupBuilder group)
    {
        // GET /game-centers — list all game centers (active + inactive)
        group.MapGet("/game-centers", async (AppDbContext db) =>
        {
            var centers = await db.GameCenters
                .OrderBy(gc => gc.Name)
                .ToListAsync();

            var centerIds = centers.Select(gc => gc.Id).ToList();

            var activeTableCounts = await db.Tables
                .IgnoreQueryFilters()
                .Where(t => centerIds.Contains(t.GameCenterId) && t.Status == TableStatus.Active)
                .GroupBy(t => t.GameCenterId)
                .Select(g => new { GameCenterId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.GameCenterId, x => x.Count);

            var response = centers.Select(gc => new GameCenterResponse
            {
                Id = gc.Id,
                Name = gc.Name,
                Location = gc.Location,
                IsActive = gc.IsActive,
                MaxTables = gc.MaxTables,
                ActiveTableCount = activeTableCounts.GetValueOrDefault(gc.Id, 0),
                CreatedAt = gc.CreatedAt,
                UpdatedAt = gc.UpdatedAt
            }).ToList();

            return Results.Ok(new ApiListResponse<GameCenterResponse>(response, response.Count));
        }).RequireAuthorization("PlatformAdminOnly");

        // POST /game-centers — create new game center
        group.MapPost("/game-centers", async (CreateGameCenterRequest request, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new ApiErrorResponse(
                    "VALIDATION_ERROR",
                    "Game center name is required."));
            }

            if (string.IsNullOrWhiteSpace(request.Location))
            {
                return Results.BadRequest(new ApiErrorResponse(
                    "VALIDATION_ERROR",
                    "Game center location is required."));
            }

            if (request.MaxTables < 1 || request.MaxTables > 200)
            {
                return Results.BadRequest(new ApiErrorResponse(
                    "VALIDATION_ERROR",
                    "Max tables must be between 1 and 200."));
            }

            var duplicateName = await db.GameCenters
                .AnyAsync(gc => gc.Name.ToLower() == request.Name.Trim().ToLower());

            if (duplicateName)
            {
                return Results.Conflict(new ApiErrorResponse(
                    "GAME_CENTER_NAME_EXISTS",
                    "A game center with this name already exists."));
            }

            var center = new GameCenter
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                Location = request.Location.Trim(),
                IsActive = true,
                MaxTables = request.MaxTables
            };

            db.GameCenters.Add(center);
            await db.SaveChangesAsync();

            var response = new GameCenterResponse
            {
                Id = center.Id,
                Name = center.Name,
                Location = center.Location,
                IsActive = center.IsActive,
                MaxTables = center.MaxTables,
                ActiveTableCount = 0,
                CreatedAt = center.CreatedAt,
                UpdatedAt = center.UpdatedAt
            };

            return Results.Created($"/api/v1/platform/game-centers/{center.Id}", new ApiResponse<GameCenterResponse>(response));
        }).RequireAuthorization("PlatformAdminOnly");

        // POST /game-centers/{id}/deactivate — deactivate a game center
        group.MapPost("/game-centers/{id}/deactivate", async (Guid id, AppDbContext db) =>
        {
            var center = await db.GameCenters.FirstOrDefaultAsync(gc => gc.Id == id);

            if (center is null)
            {
                return Results.NotFound(new ApiErrorResponse(
                    "GAME_CENTER_NOT_FOUND",
                    "The specified game center was not found."));
            }

            if (!center.IsActive)
            {
                return Results.Conflict(new ApiErrorResponse(
                    "GAME_CENTER_ALREADY_INACTIVE",
                    "This game center is already inactive."));
            }

            center.IsActive = false;
            center.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return Results.Ok(new ApiResponse<object>(new
            {
                message = "Game center deactivated successfully."
            }));
        }).RequireAuthorization("PlatformAdminOnly");

        // POST /game-centers/{id}/activate — activate a game center
        group.MapPost("/game-centers/{id}/activate", async (Guid id, AppDbContext db) =>
        {
            var center = await db.GameCenters.FirstOrDefaultAsync(gc => gc.Id == id);

            if (center is null)
            {
                return Results.NotFound(new ApiErrorResponse(
                    "GAME_CENTER_NOT_FOUND",
                    "The specified game center was not found."));
            }

            if (center.IsActive)
            {
                return Results.Conflict(new ApiErrorResponse(
                    "GAME_CENTER_ALREADY_ACTIVE",
                    "This game center is already active."));
            }

            center.IsActive = true;
            center.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return Results.Ok(new ApiResponse<object>(new
            {
                message = "Game center activated successfully."
            }));
        }).RequireAuthorization("PlatformAdminOnly");

        // PATCH /game-centers/{id}/table-slots — update table slot allocation
        group.MapPatch("/game-centers/{id}/table-slots", async (Guid id, UpdateTableSlotsRequest request, AppDbContext db) =>
        {
            if (request.MaxTables < 1 || request.MaxTables > 200)
            {
                return Results.BadRequest(new ApiErrorResponse(
                    "VALIDATION_ERROR",
                    "Max tables must be between 1 and 200."));
            }

            var center = await db.GameCenters.FirstOrDefaultAsync(gc => gc.Id == id);

            if (center is null)
            {
                return Results.NotFound(new ApiErrorResponse(
                    "GAME_CENTER_NOT_FOUND",
                    "The specified game center was not found."));
            }

            center.MaxTables = request.MaxTables;
            center.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            var activeTableCount = await db.Tables
                .IgnoreQueryFilters()
                .CountAsync(t => t.GameCenterId == id && t.Status == TableStatus.Active);

            var response = new GameCenterResponse
            {
                Id = center.Id,
                Name = center.Name,
                Location = center.Location,
                IsActive = center.IsActive,
                MaxTables = center.MaxTables,
                ActiveTableCount = activeTableCount,
                CreatedAt = center.CreatedAt,
                UpdatedAt = center.UpdatedAt
            };

            return Results.Ok(new ApiResponse<GameCenterResponse>(response));
        }).RequireAuthorization("PlatformAdminOnly");

        // POST /game-centers/{id}/tables — create a new table for a game center
        group.MapPost("/game-centers/{id}/tables", async (Guid id, AppDbContext db) =>
        {
            var center = await db.GameCenters.FirstOrDefaultAsync(gc => gc.Id == id);

            if (center is null)
            {
                return Results.NotFound(new ApiErrorResponse(
                    "GAME_CENTER_NOT_FOUND",
                    "The specified game center was not found."));
            }

            if (!center.IsActive)
            {
                return Results.Conflict(new ApiErrorResponse(
                    "GAME_CENTER_INACTIVE",
                    "Cannot create tables for an inactive game center."));
            }

            var activeTableCount = await db.Tables
                .IgnoreQueryFilters()
                .CountAsync(t => t.GameCenterId == id && t.Status == TableStatus.Active);

            if (activeTableCount >= center.MaxTables)
            {
                return Results.Conflict(new ApiErrorResponse(
                    "TABLE_LIMIT_REACHED",
                    $"This game center has reached its table allocation limit of {center.MaxTables}."));
            }

            var maxTableNumber = await db.Tables
                .IgnoreQueryFilters()
                .Where(t => t.GameCenterId == id)
                .MaxAsync(t => (int?)t.TableNumber) ?? 0;

            var tableNumber = maxTableNumber + 1;
            var shortGuid = Guid.NewGuid().ToString("N")[..8];
            var qrCode = $"gc-{id}-t-{tableNumber}-{shortGuid}";

            var table = new Table
            {
                Id = Guid.NewGuid(),
                TenantId = center.Id,
                TableNumber = tableNumber,
                Status = TableStatus.Active,
                QrCodeIdentifier = qrCode,
                GameCenterId = center.Id
            };

            db.Tables.Add(table);
            await db.SaveChangesAsync();

            var response = new TableResponse
            {
                TableId = table.Id,
                TableNumber = table.TableNumber,
                QrCodeIdentifier = table.QrCodeIdentifier,
                Status = table.Status.ToString()
            };

            return Results.Created($"/api/v1/platform/game-centers/{id}/tables/{table.Id}", new ApiResponse<TableResponse>(response));
        }).RequireAuthorization("PlatformAdminOnly");

        // GET /game-centers/{id}/tables — list all tables for a game center
        group.MapGet("/game-centers/{id}/tables", async (Guid id, AppDbContext db) =>
        {
            var center = await db.GameCenters.FirstOrDefaultAsync(gc => gc.Id == id);

            if (center is null)
            {
                return Results.NotFound(new ApiErrorResponse(
                    "GAME_CENTER_NOT_FOUND",
                    "The specified game center was not found."));
            }

            var tables = await db.Tables
                .IgnoreQueryFilters()
                .Where(t => t.GameCenterId == id)
                .OrderBy(t => t.TableNumber)
                .ToListAsync();

            var activeCount = tables.Count(t => t.Status == TableStatus.Active);
            var closedCount = tables.Count(t => t.Status == TableStatus.Closed);

            var tableResponses = tables.Select(t => new TableResponse
            {
                TableId = t.Id,
                TableNumber = t.TableNumber,
                QrCodeIdentifier = t.QrCodeIdentifier,
                Status = t.Status.ToString()
            }).ToList();

            var response = new TableListResponse
            {
                Data = tableResponses,
                Total = tables.Count,
                Summary = new TableListSummary
                {
                    ActiveCount = activeCount,
                    ClosedCount = closedCount,
                    MaxTables = center.MaxTables
                }
            };

            return Results.Ok(response);
        }).RequireAuthorization("PlatformAdminOnly");

        // GET /dashboard — platform usage dashboard stats
        group.MapGet("/dashboard", async (AppDbContext db) =>
        {
            var todayStart = DateTime.UtcNow.Date;
            var yesterdayStart = todayStart.AddDays(-1);

            var validStatuses = new[]
            {
                GameSessionStatus.Approved,
                GameSessionStatus.Active,
                GameSessionStatus.Completed,
                GameSessionStatus.Closed
            };

            // Games played yesterday
            var gamesPlayedYesterday = await db.GameSessions
                .IgnoreQueryFilters()
                .CountAsync(s => s.CreatedAt >= yesterdayStart && s.CreatedAt < todayStart
                    && validStatuses.Contains(s.Status));

            // All game centers
            var centers = await db.GameCenters
                .OrderBy(gc => gc.Name)
                .ToListAsync();

            var centerIds = centers.Select(gc => gc.Id).ToList();

            // Active tables per center
            var activeTableCounts = await db.Tables
                .IgnoreQueryFilters()
                .Where(t => centerIds.Contains(t.GameCenterId) && t.Status == TableStatus.Active)
                .GroupBy(t => t.GameCenterId)
                .Select(g => new { GameCenterId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.GameCenterId, x => x.Count);

            // Total tables per center (active + closed)
            var totalTableCounts = await db.Tables
                .IgnoreQueryFilters()
                .Where(t => centerIds.Contains(t.GameCenterId))
                .GroupBy(t => t.GameCenterId)
                .Select(g => new { GameCenterId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.GameCenterId, x => x.Count);

            // Get table IDs per center for session queries
            var tablesByCenter = await db.Tables
                .IgnoreQueryFilters()
                .Where(t => centerIds.Contains(t.GameCenterId))
                .Select(t => new { t.Id, t.GameCenterId })
                .ToListAsync();

            var tableToCenterMap = tablesByCenter.ToDictionary(t => t.Id, t => t.GameCenterId);

            var allTableIds = tablesByCenter.Select(t => t.Id).ToList();

            // Active sessions per center (Status == Active)
            var activeSessions = await db.GameSessions
                .IgnoreQueryFilters()
                .Where(s => allTableIds.Contains(s.TableId) && s.Status == GameSessionStatus.Active)
                .Select(s => new { s.TableId })
                .ToListAsync();

            // Games played today per center
            var gamesToday = await db.GameSessions
                .IgnoreQueryFilters()
                .Where(s => allTableIds.Contains(s.TableId)
                    && s.CreatedAt >= todayStart
                    && validStatuses.Contains(s.Status))
                .Select(s => new { s.TableId })
                .ToListAsync();

            // Build per-center counts using table-to-center mapping
            var activeSessionsByCenter = new Dictionary<Guid, int>();
            var gamesTodayByCenter = new Dictionary<Guid, int>();

            foreach (var session in activeSessions)
            {
                var centerId = tableToCenterMap[session.TableId];
                activeSessionsByCenter[centerId] = activeSessionsByCenter.GetValueOrDefault(centerId) + 1;
            }

            foreach (var session in gamesToday)
            {
                var centerId = tableToCenterMap[session.TableId];
                gamesTodayByCenter[centerId] = gamesTodayByCenter.GetValueOrDefault(centerId) + 1;
            }

            var totalActiveCenters = centers.Count(c => c.IsActive);
            var totalActiveTables = activeTableCounts.Values.Sum();

            var centerResponses = centers.Select(gc => new DashboardCenterResponse
            {
                Id = gc.Id,
                Name = gc.Name,
                Location = gc.Location,
                IsActive = gc.IsActive,
                ActiveTables = activeTableCounts.GetValueOrDefault(gc.Id, 0),
                TotalTables = totalTableCounts.GetValueOrDefault(gc.Id, 0),
                MaxTables = gc.MaxTables,
                ActiveSessionCount = activeSessionsByCenter.GetValueOrDefault(gc.Id, 0),
                GamesPlayedToday = gamesTodayByCenter.GetValueOrDefault(gc.Id, 0)
            }).ToList();

            var response = new PlatformDashboardResponse
            {
                GamesPlayedYesterday = gamesPlayedYesterday,
                TotalActiveCenters = totalActiveCenters,
                TotalActiveTablesAcrossPlatform = totalActiveTables,
                Centers = centerResponses
            };

            return Results.Ok(new ApiResponse<PlatformDashboardResponse>(response));
        }).RequireAuthorization("PlatformAdminOnly");

        return group;
    }
}

public class GameCenterResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int MaxTables { get; set; }
    public int ActiveTableCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreateGameCenterRequest
{
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public int MaxTables { get; set; }
}

public class UpdateTableSlotsRequest
{
    public int MaxTables { get; set; }
}

public class TableResponse
{
    public Guid TableId { get; set; }
    public int TableNumber { get; set; }
    public string QrCodeIdentifier { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class TableListResponse
{
    public List<TableResponse> Data { get; set; } = new();
    public int Total { get; set; }
    public TableListSummary Summary { get; set; } = new();
}

public class TableListSummary
{
    public int ActiveCount { get; set; }
    public int ClosedCount { get; set; }
    public int MaxTables { get; set; }
}

public class PlatformDashboardResponse
{
    public int GamesPlayedYesterday { get; set; }
    public int TotalActiveCenters { get; set; }
    public int TotalActiveTablesAcrossPlatform { get; set; }
    public List<DashboardCenterResponse> Centers { get; set; } = new();
}

public class DashboardCenterResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int ActiveTables { get; set; }
    public int TotalTables { get; set; }
    public int MaxTables { get; set; }
    public int ActiveSessionCount { get; set; }
    public int GamesPlayedToday { get; set; }
}
