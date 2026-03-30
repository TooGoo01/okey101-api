using Okey101.Api.Models.Enums;

namespace Okey101.Api.Models.Entities;

public class GameSession
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid TableId { get; set; }
    public string GameName { get; set; } = string.Empty;
    public GameSessionStatus Status { get; set; } = GameSessionStatus.Pending;
    public Guid CreatedByPlayerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int? WinnerTeamNumber { get; set; }
    public int Team1FinalTotal { get; set; }
    public int Team2FinalTotal { get; set; }

    public Table Table { get; set; } = null!;
    public Player CreatedByPlayer { get; set; } = null!;
    public ICollection<GameTeam> Teams { get; set; } = new List<GameTeam>();
}
