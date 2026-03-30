using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Okey101.Api.Configuration;
using Okey101.Api.Data;
using Okey101.Api.Models.Entities;
using Okey101.Api.Middleware;
using Okey101.Api.Models.Enums;
using Okey101.Api.Services;

namespace Okey101.Api.Tests.Services;

public class AuthServiceOtpTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly AuthService _authService;
    private readonly Mock<IOtpProvider> _otpProvider;
    private readonly Mock<IPhoneEncryptionService> _phoneEncryption;
    private const string TestPhone = "+905551234567";
    private const string TestPhoneHash = "testhash123";

    public AuthServiceOtpTests()
    {
        var tenantProvider = new TenantProvider();
        tenantProvider.SetTenantId(null);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new AppDbContext(options, tenantProvider);

        var jwtSettings = new JwtSettings
        {
            Key = "test-signing-key-that-is-at-least-32-characters-long!!",
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            AccessTokenExpirationMinutes = 60,
            RefreshTokenExpirationDays = 30
        };

        _otpProvider = new Mock<IOtpProvider>();
        _phoneEncryption = new Mock<IPhoneEncryptionService>();
        _phoneEncryption.Setup(x => x.Hash(TestPhone)).Returns(TestPhoneHash);
        _phoneEncryption.Setup(x => x.Encrypt(TestPhone)).Returns("encrypted-phone");

        _authService = new AuthService(
            _dbContext,
            Options.Create(jwtSettings),
            Mock.Of<ILogger<AuthService>>(),
            _otpProvider.Object,
            _phoneEncryption.Object);
    }

    [Fact]
    public async Task SendOtpAsync_CreatesOtpAndCallsProvider()
    {
        var result = await _authService.SendOtpAsync(TestPhone);

        Assert.True(result);
        _otpProvider.Verify(x => x.SendOtpAsync(TestPhone, It.Is<string>(code => code.Length == 6)), Times.Once);

        var storedOtp = await _dbContext.OtpCodes.FirstOrDefaultAsync(o => o.PhoneNumberHash == TestPhoneHash);
        Assert.NotNull(storedOtp);
        Assert.False(storedOtp.IsUsed);
        Assert.True(storedOtp.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task SendOtpAsync_InvalidatesPreviousOtps()
    {
        // Create an existing OTP
        _dbContext.OtpCodes.Add(new OtpCode
        {
            Id = Guid.NewGuid(),
            PhoneNumberHash = TestPhoneHash,
            CodeHash = "oldhash",
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            IsUsed = false
        });
        await _dbContext.SaveChangesAsync();

        await _authService.SendOtpAsync(TestPhone);

        var otps = await _dbContext.OtpCodes.Where(o => o.PhoneNumberHash == TestPhoneHash).ToListAsync();
        Assert.Equal(2, otps.Count);
        Assert.Single(otps, o => !o.IsUsed); // Only the new one is active
    }

    [Fact]
    public async Task VerifyOtpAsync_WithValidOtp_CreatesNewPlayer()
    {
        var otpCode = "123456";
        var codeHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(otpCode)));

        _dbContext.OtpCodes.Add(new OtpCode
        {
            Id = Guid.NewGuid(),
            PhoneNumberHash = TestPhoneHash,
            CodeHash = codeHash,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            IsUsed = false
        });
        await _dbContext.SaveChangesAsync();

        var result = await _authService.VerifyOtpAsync(TestPhone, otpCode, "New Player");

        Assert.NotNull(result);
        Assert.NotEmpty(result.AccessToken);
        Assert.NotEmpty(result.RefreshToken);
        Assert.Equal("Player", result.User.Role);

        var player = await _dbContext.Players.FirstOrDefaultAsync(p => p.PhoneNumberHash == TestPhoneHash);
        Assert.NotNull(player);
        Assert.Equal("New Player", player.Name);
    }

    [Fact]
    public async Task VerifyOtpAsync_WithExistingPlayer_ReturnsTokens()
    {
        var existingPlayer = new Player
        {
            Id = Guid.NewGuid(),
            Name = "Existing Player",
            PhoneNumber = "encrypted-phone",
            PhoneNumberHash = TestPhoneHash,
            Role = UserRole.Player
        };
        _dbContext.Players.Add(existingPlayer);

        var otpCode = "654321";
        var codeHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(otpCode)));

        _dbContext.OtpCodes.Add(new OtpCode
        {
            Id = Guid.NewGuid(),
            PhoneNumberHash = TestPhoneHash,
            CodeHash = codeHash,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            IsUsed = false
        });
        await _dbContext.SaveChangesAsync();

        var result = await _authService.VerifyOtpAsync(TestPhone, otpCode, null);

        Assert.NotNull(result);
        Assert.Equal(existingPlayer.Id.ToString(), result.User.Id);

        // Should not create a second player
        var playerCount = await _dbContext.Players.CountAsync(p => p.PhoneNumberHash == TestPhoneHash);
        Assert.Equal(1, playerCount);
    }

    [Fact]
    public async Task VerifyOtpAsync_WithExpiredOtp_ThrowsOtpExpired()
    {
        var otpCode = "111111";
        var codeHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(otpCode)));

        _dbContext.OtpCodes.Add(new OtpCode
        {
            Id = Guid.NewGuid(),
            PhoneNumberHash = TestPhoneHash,
            CodeHash = codeHash,
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1), // expired
            IsUsed = false
        });
        await _dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<OtpExpiredException>(
            () => _authService.VerifyOtpAsync(TestPhone, otpCode, "Test"));
    }

    [Fact]
    public async Task VerifyOtpAsync_WithUsedOtp_ThrowsInvalidOtp()
    {
        var otpCode = "222222";
        var codeHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(otpCode)));

        _dbContext.OtpCodes.Add(new OtpCode
        {
            Id = Guid.NewGuid(),
            PhoneNumberHash = TestPhoneHash,
            CodeHash = codeHash,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            IsUsed = true // already used
        });
        await _dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOtpException>(
            () => _authService.VerifyOtpAsync(TestPhone, otpCode, "Test"));
    }

    [Fact]
    public async Task VerifyOtpAsync_WithWrongCode_ThrowsInvalidOtp()
    {
        var codeHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("999999")));

        _dbContext.OtpCodes.Add(new OtpCode
        {
            Id = Guid.NewGuid(),
            PhoneNumberHash = TestPhoneHash,
            CodeHash = codeHash,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            IsUsed = false
        });
        await _dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOtpException>(
            () => _authService.VerifyOtpAsync(TestPhone, "000000", "Test"));
    }

    [Fact]
    public async Task VerifyOtpAsync_NewPlayerWithoutName_ThrowsArgumentException()
    {
        var otpCode = "333333";
        var codeHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(otpCode)));

        _dbContext.OtpCodes.Add(new OtpCode
        {
            Id = Guid.NewGuid(),
            PhoneNumberHash = TestPhoneHash,
            CodeHash = codeHash,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            IsUsed = false
        });
        await _dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => _authService.VerifyOtpAsync(TestPhone, otpCode, null));
    }

    [Fact]
    public async Task RefreshTokenAsync_WithValidToken_ReturnsNewTokens()
    {
        var player = new Player
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            PhoneNumber = "encrypted",
            PhoneNumberHash = "hash-refresh"
        };
        _dbContext.Players.Add(player);

        var oldToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            Token = "old-refresh-token",
            PlayerId = player.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        };
        _dbContext.RefreshTokens.Add(oldToken);
        await _dbContext.SaveChangesAsync();

        var result = await _authService.RefreshTokenAsync("old-refresh-token");

        Assert.NotNull(result);
        Assert.NotEmpty(result.AccessToken);
        Assert.NotEqual("old-refresh-token", result.RefreshToken);
        Assert.Equal(player.Id.ToString(), result.User.Id);

        // Old token should be revoked
        var revokedToken = await _dbContext.RefreshTokens.FirstAsync(rt => rt.Token == "old-refresh-token");
        Assert.NotNull(revokedToken.RevokedAt);
    }

    [Fact]
    public async Task RefreshTokenAsync_WithInvalidToken_ThrowsUnauthorized()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _authService.RefreshTokenAsync("non-existent-token"));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
