namespace Okey101.Api.Models.Entities;

public class GamePlayer
{
    public Guid Id { get; set; }
    public Guid GameTeamId { get; set; }
    public Guid? PlayerId { get; set; }
    public string? GuestName { get; set; }
    public bool IsGuest { get; set; }

    public GameTeam GameTeam { get; set; } = null!;
    public Player? Player { get; set; }
}
