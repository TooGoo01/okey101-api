using Microsoft.EntityFrameworkCore;
using Okey101.Api.Data;
using Okey101.Api.Models.Responses;

namespace Okey101.Api.Endpoints;

public static class PublicEndpoints
{
    public static RouteGroupBuilder MapPublicEndpoints(this RouteGroupBuilder group)
    {
        // GET /game-centers — list all active game centers (public, no auth)
        group.MapGet("/game-centers", async (AppDbContext db) =>
        {
            var centers = await db.GameCenters
                .Where(gc => gc.IsActive)
                .OrderBy(gc => gc.Name)
                .Select(gc => new PublicGameCenterResponse
                {
                    Id = gc.Id,
                    Name = gc.Name,
                    Location = gc.Location
                })
                .ToListAsync();

            return Results.Ok(new ApiListResponse<PublicGameCenterResponse>(centers, centers.Count));
        });

        return group;
    }
}

public class PublicGameCenterResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
}
