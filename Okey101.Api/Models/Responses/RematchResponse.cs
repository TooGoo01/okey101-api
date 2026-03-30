namespace Okey101.Api.Models.Responses;

public class RematchResponse
{
    public Guid SessionId { get; set; }
    public string GameName { get; set; } = string.Empty;
    public string Team1Name { get; set; } = string.Empty;
    public string Team2Name { get; set; } = string.Empty;
}
