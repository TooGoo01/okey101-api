namespace Okey101.Api.Models.Responses;

public class GameCompletionResponse
{
    public Guid SessionId { get; set; }
    public string Team1Name { get; set; } = string.Empty;
    public string Team2Name { get; set; } = string.Empty;
    public int Team1Total { get; set; }
    public int Team2Total { get; set; }
    public int? Winner { get; set; } // 1, 2, or null for tie
    public DateTime CompletedAt { get; set; }
}
