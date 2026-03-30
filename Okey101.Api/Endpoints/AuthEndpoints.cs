using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Okey101.Api.Data;
using Okey101.Api.Models.Enums;
using Okey101.Api.Models.Requests;
using Okey101.Api.Models.Responses;
using Okey101.Api.Services;

namespace Okey101.Api.Endpoints;

public static partial class AuthEndpoints
{
    [GeneratedRegex(@"^\+?[1-9]\d{6,14}$")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"^\d{6}$")]
    private static partial Regex OtpCodeRegex();

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/send-otp", async (SendOtpRequest request, IAuthService authService) =>
        {
            ValidatePhoneNumber(request.PhoneNumber);
            await authService.SendOtpAsync(request.PhoneNumber);
            return Results.Ok(new { data = new { message = "OTP sent successfully." } });
        });

        group.MapPost("/verify-otp", async (VerifyOtpRequest request, IAuthService authService) =>
        {
            ValidatePhoneNumber(request.PhoneNumber);
            ValidateOtpCode(request.OtpCode);
            if (request.Name is not null)
            {
                request.Name = request.Name.Trim();
                if (request.Name.Length < 2 || request.Name.Length > 100)
                {
                    throw new ArgumentException("Name must be between 2 and 100 characters.");
                }
            }

            var result = await authService.VerifyOtpAsync(request.PhoneNumber, request.OtpCode, request.Name);
            return Results.Ok(new { data = result });
        });

        group.MapPost("/refresh", async (RefreshTokenRequest request, IAuthService authService) =>
        {
            if (string.IsNullOrWhiteSpace(request.RefreshToken))
            {
                throw new ArgumentException("Refresh token is required.");
            }

            var result = await authService.RefreshTokenAsync(request.RefreshToken);
            return Results.Ok(new { data = result });
        });

        group.MapGet("/me", async (HttpContext httpContext, AppDbContext db) =>
        {
            var userIdClaim = httpContext.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            if (userIdClaim is null || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Results.Unauthorized();
            }

            var player = await db.Players
                .IgnoreQueryFilters()
                .Where(p => p.Id == userId)
                .Select(p => new { p.Id, p.Name, p.Role, p.TenantId })
                .FirstOrDefaultAsync();

            if (player is null)
            {
                return Results.NotFound(new ApiErrorResponse("USER_NOT_FOUND", "User not found."));
            }

            string? gameCenterName = null;
            if (player.TenantId.HasValue)
            {
                gameCenterName = await db.GameCenters
                    .IgnoreQueryFilters()
                    .Where(gc => gc.Id == player.TenantId.Value)
                    .Select(gc => gc.Name)
                    .FirstOrDefaultAsync();
            }

            return Results.Ok(new ApiResponse<object>(new
            {
                playerId = player.Id,
                name = player.Name,
                role = player.Role.ToString(),
                tenantId = player.TenantId,
                gameCenterName
            }));
        }).RequireAuthorization();

        return group;
    }

    private static void ValidatePhoneNumber(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber) || !PhoneRegex().IsMatch(phoneNumber))
        {
            throw new ArgumentException("Invalid phone number format. Expected: international format (e.g., +905551234567).");
        }
    }

    private static void ValidateOtpCode(string? otpCode)
    {
        if (string.IsNullOrWhiteSpace(otpCode) || !OtpCodeRegex().IsMatch(otpCode))
        {
            throw new ArgumentException("OTP code must be exactly 6 digits.");
        }
    }
}
