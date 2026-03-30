using System.Security.Claims;
using Okey101.Api.Models.Enums;
using Okey101.Api.Services;

namespace Okey101.Api.Middleware;

public class TenantMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantMiddleware> _logger;

    public TenantMiddleware(RequestDelegate next, ILogger<TenantMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ITenantProvider tenantProvider)
    {
        // Always reset tenant to prevent AsyncLocal leaking from previous requests
        tenantProvider.SetTenantId(null);

        if (context.User.Identity?.IsAuthenticated == true)
        {
            var roleClaim = context.User.FindFirstValue(ClaimTypes.Role);
            var tenantClaim = context.User.FindFirstValue("tenantId");

            if (roleClaim == UserRole.PlatformAdmin.ToString())
            {
                // PlatformAdmin bypasses tenant filter — null TenantId
                tenantProvider.SetTenantId(null);
                _logger.LogDebug("PlatformAdmin access — tenant filter bypassed");
            }
            else if (Guid.TryParse(tenantClaim, out var tenantId))
            {
                tenantProvider.SetTenantId(tenantId);
            }
            else
            {
                // Players without a tenant — set a non-matching Guid to ensure isolation
                // (players see only their own data via other filters, not tenant filter)
                tenantProvider.SetTenantId(Guid.Empty);
            }
        }

        await _next(context);
    }
}
