using Okey101.Api.Models.Enums;

namespace Okey101.Api.Models.Entities;

public class ScoreEntry
{
    public Guid Id { get; set; }
    public Guid GameSessionId { get; set; }
    public int TeamNumber { get; set; }
    public int RoundNumber { get; set; }
    public ScoreType ScoreType { get; set; }
    public int Value { get; set; }
    public Guid CreatedByPlayerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsRemoved { get; set; }
    public DateTime? RemovedAt { get; set; }

    public GameSession GameSession { get; set; } = null!;
    public Player CreatedByPlayer { get; set; } = null!;
}
