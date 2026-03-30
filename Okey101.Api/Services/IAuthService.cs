using Okey101.Api.Models.Entities;
using Okey101.Api.Models.Responses;

namespace Okey101.Api.Services;

public interface IAuthService
{
    AuthTokenResponse GenerateAccessToken(Player player);
    Task<RefreshToken> GenerateRefreshTokenAsync(Player player);
    Task<Player?> ValidateRefreshTokenAsync(string token);
    Task<bool> SendOtpAsync(string phoneNumber);
    Task<AuthResult> VerifyOtpAsync(string phoneNumber, string otpCode, string? name);
    Task<AuthResult> RefreshTokenAsync(string refreshToken);
}
