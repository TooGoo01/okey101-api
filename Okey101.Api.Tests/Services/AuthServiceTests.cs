using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Okey101.Api.Configuration;
using Okey101.Api.Data;
using Okey101.Api.Models.Entities;
using Okey101.Api.Models.Enums;
using Okey101.Api.Services;

namespace Okey101.Api.Tests.Services;

public class AuthServiceTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly AuthService _authService;
    private readonly JwtSettings _jwtSettings;

    public AuthServiceTests()
    {
        var tenantProvider = new TenantProvider();
        tenantProvider.SetTenantId(null); // superadmin for tests

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new AppDbContext(options, tenantProvider);

        _jwtSettings = new JwtSettings
        {
            Key = "test-signing-key-that-is-at-least-32-characters-long!!",
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            AccessTokenExpirationMinutes = 60,
            RefreshTokenExpirationDays = 30
        };

        _authService = new AuthService(
            _dbContext,
            Options.Create(_jwtSettings),
            Mock.Of<ILogger<AuthService>>(),
            Mock.Of<IOtpProvider>(),
            Mock.Of<IPhoneEncryptionService>());
    }

    [Fact]
    public void GenerateAccessToken_ReturnsValidToken()
    {
        var player = new Player
        {
            Id = Guid.NewGuid(),
            Name = "Test Player",
            Role = UserRole.Player,
            TenantId = Guid.NewGuid()
        };

        var result = _authService.GenerateAccessToken(player);

        Assert.NotNull(result.AccessToken);
        Assert.NotEmpty(result.AccessToken);
        Assert.True(result.ExpiresAt > DateTime.UtcNow);
        Assert.True(result.ExpiresAt <= DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes + 1));
    }

    [Fact]
    public void GenerateAccessToken_ForPlatformAdmin_DoesNotIncludeTenantClaim()
    {
        var player = new Player
        {
            Id = Guid.NewGuid(),
            Name = "Admin",
            Role = UserRole.PlatformAdmin,
            TenantId = null
        };

        var result = _authService.GenerateAccessToken(player);

        Assert.NotNull(result.AccessToken);
        // Token should not contain tenantId claim for admin without tenant
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result.AccessToken);
        Assert.DoesNotContain(token.Claims, c => c.Type == "tenantId");
    }

    [Fact]
    public async Task GenerateRefreshToken_StoresTokenInDatabase()
    {
        var player = new Player
        {
            Id = Guid.NewGuid(),
            Name = "Test Player",
            PhoneNumber = "encrypted",
            PhoneNumberHash = "hash123"
        };
        _dbContext.Players.Add(player);
        await _dbContext.SaveChangesAsync();

        var refreshToken = await _authService.GenerateRefreshTokenAsync(player);

        Assert.NotNull(refreshToken.Token);
        Assert.Equal(player.Id, refreshToken.PlayerId);
        Assert.True(refreshToken.ExpiresAt > DateTime.UtcNow);

        var stored = await _dbContext.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == refreshToken.Token);
        Assert.NotNull(stored);
    }

    [Fact]
    public async Task ValidateRefreshToken_WithValidToken_ReturnsPlayer()
    {
        var player = new Player
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            PhoneNumber = "encrypted",
            PhoneNumberHash = "hash456"
        };
        _dbContext.Players.Add(player);

        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            Token = "valid-refresh-token",
            PlayerId = player.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        };
        _dbContext.RefreshTokens.Add(token);
        await _dbContext.SaveChangesAsync();

        var result = await _authService.ValidateRefreshTokenAsync("valid-refresh-token");

        Assert.NotNull(result);
        Assert.Equal(player.Id, result.Id);
    }

    [Fact]
    public async Task ValidateRefreshToken_WithExpiredToken_ReturnsNull()
    {
        var player = new Player
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            PhoneNumber = "encrypted",
            PhoneNumberHash = "hash789"
        };
        _dbContext.Players.Add(player);

        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            Token = "expired-token",
            PlayerId = player.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(-1) // expired
        };
        _dbContext.RefreshTokens.Add(token);
        await _dbContext.SaveChangesAsync();

        var result = await _authService.ValidateRefreshTokenAsync("expired-token");

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateRefreshToken_WithInvalidToken_ReturnsNull()
    {
        var result = await _authService.ValidateRefreshTokenAsync("non-existent-token");

        Assert.Null(result);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
