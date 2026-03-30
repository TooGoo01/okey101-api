using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Okey101.Api.Middleware;

namespace Okey101.Api.Tests.Middleware;

public class RateLimitMiddlewareTests
{
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly Mock<ILogger<RateLimitMiddleware>> _logger = new();
    private bool _nextCalled;

    private RateLimitMiddleware CreateMiddleware()
    {
        return new RateLimitMiddleware(
            next: _ => { _nextCalled = true; return Task.CompletedTask; },
            _cache,
            _logger.Object);
    }

    private static HttpContext CreateOtpContext(string phone, string path = "/api/v1/auth/send-otp")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = "POST";
        context.Request.ContentType = "application/json";

        var body = JsonSerializer.Serialize(new { phoneNumber = phone });
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Response.Body = new MemoryStream();

        return context;
    }

    [Fact]
    public async Task InvokeAsync_NonOtpEndpoint_PassesThrough()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/players";

        await middleware.InvokeAsync(context);

        Assert.True(_nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_OtpEndpoint_UnderLimit_PassesThrough()
    {
        var middleware = CreateMiddleware();
        var context = CreateOtpContext("+994501234567");

        await middleware.InvokeAsync(context);

        Assert.True(_nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_OtpEndpoint_AtLimit_Returns429()
    {
        var middleware = CreateMiddleware();
        var phone = "+994501234568";

        // Make 5 requests (the limit)
        for (int i = 0; i < 5; i++)
        {
            _nextCalled = false;
            var ctx = CreateOtpContext(phone);
            await middleware.InvokeAsync(ctx);
            Assert.True(_nextCalled);
        }

        // 6th request should be rate limited
        _nextCalled = false;
        var context = CreateOtpContext(phone);
        await middleware.InvokeAsync(context);

        Assert.False(_nextCalled);
        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_VerifyOtpEndpoint_HasIndependentRateLimit()
    {
        var middleware = CreateMiddleware();
        var phone = "+994501234569";

        // Exhaust limit via send-otp
        for (int i = 0; i < 5; i++)
        {
            var ctx = CreateOtpContext(phone, "/api/v1/auth/send-otp");
            await middleware.InvokeAsync(ctx);
        }

        // verify-otp should NOT be blocked — separate counter from send-otp
        _nextCalled = false;
        var context = CreateOtpContext(phone, "/api/v1/auth/verify-otp");
        await middleware.InvokeAsync(context);

        Assert.True(_nextCalled);
        Assert.NotEqual(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_DifferentPhones_IndependentLimits()
    {
        var middleware = CreateMiddleware();

        // Exhaust limit for phone1
        for (int i = 0; i < 5; i++)
        {
            var ctx = CreateOtpContext("+994501111111");
            await middleware.InvokeAsync(ctx);
        }

        // phone2 should still work
        _nextCalled = false;
        var context = CreateOtpContext("+994502222222");
        await middleware.InvokeAsync(context);

        Assert.True(_nextCalled);
    }
}
