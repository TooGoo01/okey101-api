namespace Okey101.Api.Models.Requests;

public class VerifyOtpRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string OtpCode { get; set; } = string.Empty;
    public string? Name { get; set; }
}
