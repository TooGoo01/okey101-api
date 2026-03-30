using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Okey101.Api.Middleware;
using Okey101.Api.Models.Enums;
using Okey101.Api.Services;

namespace Okey101.Api.Tests.Middleware;

public class TenantMiddlewareTests
{
    private readonly Mock<ILogger<TenantMiddleware>> _logger = new();
    private readonly TenantProvider _tenantProvider = new();
    private bool _nextCalled;
    private Guid? _capturedTenantId;

    private TenantMiddleware CreateMiddleware()
    {
        return new TenantMiddleware(
            next: _ =>
            {
                _nextCalled = true;
                _capturedTenantId = _tenantProvider.TenantId;
                return Task.CompletedTask;
            },
            _logger.Object);
    }

    [Fact]
    public async Task InvokeAsync_UnauthenticatedRequest_SkipsTenantResolution()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext(); // no user

        await middleware.InvokeAsync(context, _tenantProvider);

        Assert.True(_nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_PlatformAdmin_SetsNullTenantId()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.User = CreateUser(UserRole.PlatformAdmin, null);

        await middleware.InvokeAsync(context, _tenantProvider);

        Assert.True(_nextCalled);
        Assert.Null(_capturedTenantId);
    }

    [Fact]
    public async Task InvokeAsync_GameCenterAdmin_SetsTenantId()
    {
        var middleware = CreateMiddleware();
        var tenantId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.User = CreateUser(UserRole.GameCenterAdmin, tenantId);

        await middleware.InvokeAsync(context, _tenantProvider);

        Assert.True(_nextCalled);
        Assert.Equal(tenantId, _capturedTenantId);
    }

    [Fact]
    public async Task InvokeAsync_PlayerWithoutTenant_SetsEmptyGuid()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.User = CreateUser(UserRole.Player, null);

        await middleware.InvokeAsync(context, _tenantProvider);

        Assert.True(_nextCalled);
        Assert.Equal(Guid.Empty, _capturedTenantId);
    }

    private static ClaimsPrincipal CreateUser(UserRole role, Guid? tenantId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new(ClaimTypes.Role, role.ToString())
        };

        if (tenantId.HasValue)
        {
            claims.Add(new Claim("tenantId", tenantId.Value.ToString()));
        }

        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }
}
