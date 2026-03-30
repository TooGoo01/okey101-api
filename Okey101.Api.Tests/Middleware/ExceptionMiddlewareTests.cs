using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Okey101.Api.Middleware;
using Okey101.Api.Models.Responses;

namespace Okey101.Api.Tests.Middleware;

public class ExceptionMiddlewareTests
{
    private readonly Mock<ILogger<ExceptionMiddleware>> _logger = new();

    [Fact]
    public async Task InvokeAsync_NoException_PassesThrough()
    {
        var middleware = new ExceptionMiddleware(
            next: _ => Task.CompletedTask,
            _logger.Object);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(200, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_ArgumentException_Returns400()
    {
        var middleware = new ExceptionMiddleware(
            next: _ => throw new ArgumentException("Invalid input"),
            _logger.Object);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal((int)HttpStatusCode.BadRequest, context.Response.StatusCode);
        var response = await ReadResponse(context);
        Assert.Equal("VALIDATION_ERROR", response?.Error?.Code);
    }

    [Fact]
    public async Task InvokeAsync_UnauthorizedAccessException_Returns401()
    {
        var middleware = new ExceptionMiddleware(
            next: _ => throw new UnauthorizedAccessException("Not authorized"),
            _logger.Object);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal((int)HttpStatusCode.Unauthorized, context.Response.StatusCode);
        var response = await ReadResponse(context);
        Assert.Equal("UNAUTHORIZED", response?.Error?.Code);
    }

    [Fact]
    public async Task InvokeAsync_KeyNotFoundException_Returns404()
    {
        var middleware = new ExceptionMiddleware(
            next: _ => throw new KeyNotFoundException("Not found"),
            _logger.Object);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal((int)HttpStatusCode.NotFound, context.Response.StatusCode);
        var response = await ReadResponse(context);
        Assert.Equal("NOT_FOUND", response?.Error?.Code);
    }

    [Fact]
    public async Task InvokeAsync_GenericException_Returns500()
    {
        var middleware = new ExceptionMiddleware(
            next: _ => throw new Exception("Something broke"),
            _logger.Object);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal((int)HttpStatusCode.InternalServerError, context.Response.StatusCode);
        var response = await ReadResponse(context);
        Assert.Equal("INTERNAL_ERROR", response?.Error?.Code);
        Assert.Equal("An unexpected error occurred.", response?.Error?.Message);
    }

    [Fact]
    public async Task InvokeAsync_ReturnsJsonContentType()
    {
        var middleware = new ExceptionMiddleware(
            next: _ => throw new Exception("Error"),
            _logger.Object);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal("application/json", context.Response.ContentType);
    }

    private static async Task<ApiErrorResponse?> ReadResponse(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();
        return JsonSerializer.Deserialize<ApiErrorResponse>(body, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}
