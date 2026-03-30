namespace Okey101.Api.Models.Responses;

public class GameSessionResponse
{
    public Guid SessionId { get; set; }
    public string GameName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public int? TableNumber { get; set; }
    public string? GameCenterName { get; set; }
    public string? CreatedByPlayerName { get; set; }
    public List<GameTeamResponse> Teams { get; set; } = new();
    public int? WinnerTeamNumber { get; set; }
    public int Team1FinalTotal { get; set; }
    public int Team2FinalTotal { get; set; }
    public DateTime? EndedAt { get; set; }
}

public class NewGameNotification
{
    public Guid SessionId { get; set; }
    public string GameName { get; set; } = string.Empty;
    public int TableNumber { get; set; }
    public string GameCenterName { get; set; } = string.Empty;
    public int PlayerCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class GameTeamResponse
{
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int TeamNumber { get; set; }
    public List<GamePlayerResponse> Players { get; set; } = new();
}

public class GamePlayerResponse
{
    public Guid PlayerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsGuest { get; set; }
}

public class AdminGameSessionResponse : GameSessionResponse
{
    public int CurrentRound { get; set; }
}
