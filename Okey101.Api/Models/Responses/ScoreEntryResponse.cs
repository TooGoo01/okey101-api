namespace Okey101.Api.Models.Responses;

public class ScoreEntryResponse
{
    public Guid Id { get; set; }
    public Guid GameSessionId { get; set; }
    public int TeamNumber { get; set; }
    public int RoundNumber { get; set; }
    public string ScoreType { get; set; } = string.Empty;
    public int Value { get; set; }
    public Guid CreatedByPlayerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsRemoved { get; set; }
    public DateTime? RemovedAt { get; set; }
}
