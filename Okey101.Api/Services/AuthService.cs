using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Okey101.Api.Configuration;
using Okey101.Api.Data;
using Okey101.Api.Middleware;
using Okey101.Api.Models.Entities;
using Okey101.Api.Models.Enums;
using Okey101.Api.Models.Responses;

namespace Okey101.Api.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _dbContext;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<AuthService> _logger;
    private readonly IOtpProvider _otpProvider;
    private readonly IPhoneEncryptionService _phoneEncryption;

    public AuthService(
        AppDbContext dbContext,
        IOptions<JwtSettings> jwtSettings,
        ILogger<AuthService> logger,
        IOtpProvider otpProvider,
        IPhoneEncryptionService phoneEncryption)
    {
        _dbContext = dbContext;
        _jwtSettings = jwtSettings.Value;
        _logger = logger;
        _otpProvider = otpProvider;
        _phoneEncryption = phoneEncryption;
    }

    public AuthTokenResponse GenerateAccessToken(Player player)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, player.Id.ToString()),
            new(ClaimTypes.Role, player.Role.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (player.TenantId.HasValue)
        {
            claims.Add(new Claim("tenantId", player.TenantId.Value.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        _logger.LogInformation("Generated access token for player {PlayerId} with role {Role}", player.Id, player.Role);

        return new AuthTokenResponse
        {
            AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
            ExpiresAt = expiresAt
        };
    }

    public async Task<RefreshToken> GenerateRefreshTokenAsync(Player player)
    {
        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            PlayerId = player.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays),
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.RefreshTokens.Add(refreshToken);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Generated refresh token for player {PlayerId}", player.Id);

        return refreshToken;
    }

    public async Task<Player?> ValidateRefreshTokenAsync(string token)
    {
        var refreshToken = await _dbContext.RefreshTokens
            .Include(rt => rt.Player)
            .FirstOrDefaultAsync(rt => rt.Token == token);

        if (refreshToken == null || !refreshToken.IsActive)
        {
            _logger.LogWarning("Invalid or expired refresh token attempted");
            return null;
        }

        return refreshToken.Player;
    }

    public async Task<bool> SendOtpAsync(string phoneNumber)
    {
        var phoneHash = _phoneEncryption.Hash(phoneNumber);

        // Invalidate any existing unused OTPs for this phone
        var existingOtps = await _dbContext.OtpCodes
            .Where(o => o.PhoneNumberHash == phoneHash && !o.IsUsed)
            .ToListAsync();

        foreach (var existing in existingOtps)
        {
            existing.IsUsed = true;
        }

        // Generate 6-digit OTP
        var otpCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var codeHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(otpCode)));

        var otp = new OtpCode
        {
            Id = Guid.NewGuid(),
            PhoneNumberHash = phoneHash,
            CodeHash = codeHash,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            IsUsed = false,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.OtpCodes.Add(otp);
        await _dbContext.SaveChangesAsync();

        await _otpProvider.SendOtpAsync(phoneNumber, otpCode);

        _logger.LogInformation("OTP sent for phone hash {PhoneHash}", phoneHash[..8]);
        return true;
    }

    public async Task<AuthResult> VerifyOtpAsync(string phoneNumber, string otpCode, string? name)
    {
        var phoneHash = _phoneEncryption.Hash(phoneNumber);
        var codeHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(otpCode)));

        // Step 1: Find OTP by phone + code hash (regardless of expiry) to distinguish error cases
        var otp = await _dbContext.OtpCodes
            .Where(o => o.PhoneNumberHash == phoneHash
                && o.CodeHash == codeHash
                && !o.IsUsed)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        if (otp == null)
        {
            throw new InvalidOtpException("Invalid OTP code.");
        }

        // Step 2: Check expiry separately for distinct error code (AC5)
        if (otp.ExpiresAt <= DateTime.UtcNow)
        {
            throw new OtpExpiredException("OTP code has expired.");
        }

        // Step 3: Mark as used (concurrent requests on same OTP will see IsUsed=true
        // on their read since EF tracks the entity, and SaveChanges will persist first writer;
        // second writer finds no matching unused OTP above)
        otp.IsUsed = true;

        // Find or create player — no IgnoreQueryFilters; anonymous requests
        // bypass tenant filter by design (TenantId == null). Future status
        // filters (e.g. IsActive) will correctly exclude deactivated players.
        var player = await _dbContext.Players
            .FirstOrDefaultAsync(p => p.PhoneNumberHash == phoneHash);

        if (player == null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Name is required for new player registration.");
            }

            player = new Player
            {
                Id = Guid.NewGuid(),
                Name = name,
                PhoneNumber = _phoneEncryption.Encrypt(phoneNumber),
                PhoneNumberHash = phoneHash,
                Role = UserRole.Player,
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.Players.Add(player);
            _logger.LogInformation("New player registered: {PlayerId}", player.Id);
        }

        await _dbContext.SaveChangesAsync();

        // Generate tokens
        var accessToken = GenerateAccessToken(player);
        var refreshToken = await GenerateRefreshTokenAsync(player);

        return new AuthResult
        {
            AccessToken = accessToken.AccessToken,
            RefreshToken = refreshToken.Token,
            ExpiresAt = accessToken.ExpiresAt,
            User = new AuthUserInfo
            {
                Id = player.Id.ToString(),
                Role = player.Role.ToString(),
                TenantId = player.TenantId?.ToString()
            }
        };
    }

    public async Task<AuthResult> RefreshTokenAsync(string refreshToken)
    {
        // Single fetch: load token + player, then validate and revoke atomically
        var oldToken = await _dbContext.RefreshTokens
            .Include(rt => rt.Player)
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken);

        if (oldToken == null || !oldToken.IsActive)
        {
            throw new UnauthorizedAccessException("Invalid or expired refresh token.");
        }

        // Revoke immediately on the same entity to avoid double-fetch race
        oldToken.RevokedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        var player = oldToken.Player;

        // Generate new tokens
        var newAccessToken = GenerateAccessToken(player);
        var newRefreshToken = await GenerateRefreshTokenAsync(player);

        return new AuthResult
        {
            AccessToken = newAccessToken.AccessToken,
            RefreshToken = newRefreshToken.Token,
            ExpiresAt = newAccessToken.ExpiresAt,
            User = new AuthUserInfo
            {
                Id = player.Id.ToString(),
                Role = player.Role.ToString(),
                TenantId = player.TenantId?.ToString()
            }
        };
    }
}
