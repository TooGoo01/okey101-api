namespace Okey101.Api.Models.Requests;

public class CreateGameRequest
{
    public string GameName { get; set; } = string.Empty;
    public Guid TableId { get; set; }
    public Guid GameCenterId { get; set; }
    public List<CreateGameTeamRequest> Teams { get; set; } = new();
}

public class CreateGameTeamRequest
{
    public string TeamName { get; set; } = string.Empty;
    public List<CreateGamePlayerRequest> Players { get; set; } = new();
}

public class CreateGamePlayerRequest
{
    public Guid? PlayerId { get; set; }
    public string? GuestName { get; set; }
    public bool IsGuest { get; set; }
}
