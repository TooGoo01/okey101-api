namespace Okey101.Api.Models.Responses;

public class AuthResult
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public AuthUserInfo User { get; set; } = new();
}

public class AuthUserInfo
{
    public string Id { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? TenantId { get; set; }
}
