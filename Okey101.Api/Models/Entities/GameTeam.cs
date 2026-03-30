namespace Okey101.Api.Models.Entities;

public class GameTeam
{
    public Guid Id { get; set; }
    public Guid GameSessionId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int TeamNumber { get; set; }

    public GameSession GameSession { get; set; } = null!;
    public ICollection<GamePlayer> Players { get; set; } = new List<GamePlayer>();
}
