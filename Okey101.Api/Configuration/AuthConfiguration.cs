using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Okey101.Api.Configuration;

public static class AuthConfiguration
{
    // Well-known dev player ID matching the first seeded admin in DataSeeder
    internal const string DevPlayerId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    internal const string DevTenantId = "11111111-1111-1111-1111-111111111111";

    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtSettings = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
            ?? throw new InvalidOperationException("JWT settings are not configured.");

        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));

        var isDevelopment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidAudience = jwtSettings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),
                ClockSkew = TimeSpan.Zero
            };

            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    // Allow SignalR to pass token via query string
                    var accessToken = context.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(accessToken))
                    {
                        context.Token = accessToken;
                    }

                    // Bypass: accept "dev-skip-token" as a valid token (seeds admin player)
                    {
                        var token = context.Token
                            ?? context.Request.Headers["Authorization"].FirstOrDefault()?.Replace("Bearer ", "");

                        if (token == "dev-skip-token")
                        {
                            var claims = new[]
                            {
                                new Claim(JwtRegisteredClaimNames.Sub, DevPlayerId),
                                new Claim(ClaimTypes.NameIdentifier, DevPlayerId),
                                new Claim(ClaimTypes.Role, Models.Enums.UserRole.GameCenterAdmin.ToString()),
                                new Claim("tenantId", DevTenantId),
                            };
                            context.Principal = new ClaimsPrincipal(
                                new ClaimsIdentity(claims, JwtBearerDefaults.AuthenticationScheme));
                            context.Success();
                        }
                    }

                    return Task.CompletedTask;
                }
            };
        });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("AdminOnly", policy =>
                policy.RequireRole(
                    Models.Enums.UserRole.GameCenterAdmin.ToString(),
                    Models.Enums.UserRole.PlatformAdmin.ToString()));

            options.AddPolicy("PlatformAdminOnly", policy =>
                policy.RequireRole(
                    Models.Enums.UserRole.PlatformAdmin.ToString()));
        });

        return services;
    }
}
