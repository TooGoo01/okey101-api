using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Okey101.Api.Models.Responses;

namespace Okey101.Api.Middleware;

public class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RateLimitMiddleware> _logger;

    private const int MaxAttempts = 5;
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    public RateLimitMiddleware(RequestDelegate next, IMemoryCache cache, ILogger<RateLimitMiddleware> logger)
    {
        _next = next;
        _cache = cache;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;

        if (!IsOtpEndpoint(path))
        {
            await _next(context);
            return;
        }

        // Read phone from request body (peek without consuming)
        context.Request.EnableBuffering();
        var phoneNumber = await ExtractPhoneNumberAsync(context.Request);
        context.Request.Body.Position = 0;

        // Separate rate limit keys for send vs verify to avoid cross-counting
        var endpointPrefix = path.Contains("/auth/verify-otp") ? "otp_verify" : "otp_send";
        var cacheKey = string.IsNullOrEmpty(phoneNumber)
            ? $"{endpointPrefix}:ip:{context.Connection.RemoteIpAddress}"
            : $"{endpointPrefix}:{phoneNumber}";
        var attempts = _cache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Window;
            return 0;
        });

        if (attempts >= MaxAttempts)
        {
            _logger.LogWarning("OTP rate limit exceeded for cache key {CacheKey}", cacheKey.Contains(":ip:") ? cacheKey : $"{endpointPrefix}:phone:***");

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;

            var response = new ApiErrorResponse("RATE_LIMITED", "Too many OTP attempts. Please try again later.");
            var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await context.Response.WriteAsync(json);
            return;
        }

        // For send-otp: increment before execution (prevent SMS flooding)
        // For verify-otp: increment after execution only on failure (don't penalize successful logins)
        var isVerify = path.Contains("/auth/verify-otp");
        if (!isVerify)
        {
            _cache.Set(cacheKey, attempts + 1, Window);
        }

        await _next(context);

        if (isVerify && context.Response.StatusCode != StatusCodes.Status200OK)
        {
            _cache.Set(cacheKey, attempts + 1, Window);
        }
    }

    private static bool IsOtpEndpoint(string path)
    {
        return path.Contains("/auth/send-otp") || path.Contains("/auth/verify-otp");
    }

    private static async Task<string?> ExtractPhoneNumberAsync(HttpRequest request)
    {
        if (request.ContentLength == null || request.ContentLength == 0)
            return null;

        try
        {
            using var reader = new StreamReader(request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("phoneNumber", out var phoneElement))
                return phoneElement.GetString();
        }
        catch
        {
            // If we can't parse, skip rate limiting
        }

        return null;
    }
}
