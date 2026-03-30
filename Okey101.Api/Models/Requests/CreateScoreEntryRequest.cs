namespace Okey101.Api.Models.Requests;

public class CreateScoreEntryRequest
{
    public int TeamNumber { get; set; }
    public int RoundNumber { get; set; }
    public string ScoreType { get; set; } = string.Empty;
    public int Value { get; set; }
}
