namespace Okey101.Api.Models.Responses;

public class AdminTableResponse
{
    public Guid TableId { get; set; }
    public int TableNumber { get; set; }
    public string DisplayStatus { get; set; } = "Empty";
    public TableSessionInfo? CurrentSession { get; set; }
}

public class TableSessionInfo
{
    public Guid SessionId { get; set; }
    public string GameName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<GameTeamResponse> Teams { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}
