namespace Okey101.Api.Models.Responses;

public class TableResolveResponse
{
    public Guid TableId { get; set; }
    public int TableNumber { get; set; }
    public Guid GameCenterId { get; set; }
    public string GameCenterName { get; set; } = string.Empty;
}
