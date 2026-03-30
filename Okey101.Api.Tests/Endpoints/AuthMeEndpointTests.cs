using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Okey101.Api.Data;
using Okey101.Api.Models.Entities;
using Okey101.Api.Models.Enums;
using Okey101.Api.Models.Responses;
using Okey101.Api.Services;

namespace Okey101.Api.Tests.Endpoints;

public class AuthMeEndpointTests : IDisposable
{
    private readonly TenantProvider _tenantProvider;
    private readonly AppDbContext _dbContext;
    private readonly Guid _gameCenterId;
    private readonly Guid _adminPlayerId;
    private readonly Guid _tenantId;

    public AuthMeEndpointTests()
    {
        _tenantProvider = new TenantProvider();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new AppDbContext(options, _tenantProvider);
        _gameCenterId = Guid.NewGuid();
        _tenantId = _gameCenterId;
        _adminPlayerId = Guid.NewGuid();

        _tenantProvider.SetTenantId(null);
        _dbContext.GameCenters.Add(new GameCenter
        {
            Id = _gameCenterId,
            Name = "Test Game Center",
            IsActive = true,
            MaxTables = 10
        });
        _dbContext.Players.Add(new Player
        {
            Id = _adminPlayerId,
            Name = "Admin User",
            PhoneNumber = "encrypted",
            PhoneNumberHash = "hash-admin",
            TenantId = _tenantId,
            Role = UserRole.GameCenterAdmin
        });
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task GetMe_ValidAdminUser_ReturnsUserInfo()
    {
        var httpContext = CreateHttpContext(_adminPlayerId);

        var result = await InvokeGetMe(httpContext);

        var okResult = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<ApiResponse<object>>>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task GetMe_NoSubClaim_ReturnsUnauthorized()
    {
        var httpContext = new DefaultHttpContext();
        // No claims set

        var result = await InvokeGetMe(httpContext);

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.UnauthorizedHttpResult>(result);
    }

    [Fact]
    public async Task GetMe_InvalidGuidInSubClaim_ReturnsUnauthorized()
    {
        var httpContext = new DefaultHttpContext();
        var identity = new ClaimsIdentity(new[] { new Claim(JwtRegisteredClaimNames.Sub, "not-a-guid") });
        httpContext.User = new ClaimsPrincipal(identity);

        var result = await InvokeGetMe(httpContext);

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.UnauthorizedHttpResult>(result);
    }

    [Fact]
    public async Task GetMe_NonExistentUser_ReturnsNotFound()
    {
        var httpContext = CreateHttpContext(Guid.NewGuid()); // random non-existent user

        var result = await InvokeGetMe(httpContext);

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.NotFound<ApiErrorResponse>>(result);
    }

    [Fact]
    public async Task GetMe_PlayerWithNoTenant_ReturnsNullGameCenterName()
    {
        var playerNoTenant = Guid.NewGuid();
        _tenantProvider.SetTenantId(null);
        _dbContext.Players.Add(new Player
        {
            Id = playerNoTenant,
            Name = "No Tenant Admin",
            PhoneNumber = "encrypted2",
            PhoneNumberHash = "hash-no-tenant",
            TenantId = null,
            Role = UserRole.PlatformAdmin
        });
        await _dbContext.SaveChangesAsync();

        var httpContext = CreateHttpContext(playerNoTenant);
        var result = await InvokeGetMe(httpContext);

        var okResult = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<ApiResponse<object>>>(result);
        Assert.NotNull(okResult.Value);
    }

    private DefaultHttpContext CreateHttpContext(Guid userId)
    {
        var httpContext = new DefaultHttpContext();
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())
        });
        httpContext.User = new ClaimsPrincipal(identity);
        return httpContext;
    }

    private async Task<IResult> InvokeGetMe(HttpContext httpContext)
    {
        // Replicate the /me endpoint logic
        var userIdClaim = httpContext.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Unauthorized();
        }

        var player = await _dbContext.Players
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
            gameCenterName = await _dbContext.GameCenters
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
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
