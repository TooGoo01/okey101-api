namespace Okey101.Api.Models.Entities;

public class OtpCode
{
    public Guid Id { get; set; }
    public string PhoneNumberHash { get; set; } = string.Empty;
    public string CodeHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
