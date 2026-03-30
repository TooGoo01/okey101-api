using System.Net;
using System.Text.Json;
using Okey101.Api.Models.Responses;

namespace Okey101.Api.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, errorCode, clientMessage) = exception switch
        {
            InvalidOtpException
                => (HttpStatusCode.BadRequest, "INVALID_OTP", exception.Message),
            OtpExpiredException
                => (HttpStatusCode.BadRequest, "OTP_EXPIRED", exception.Message),
            ArgumentException or FluentValidationException
                => (HttpStatusCode.BadRequest, "VALIDATION_ERROR", exception.Message),
            UnauthorizedAccessException
                => (HttpStatusCode.Unauthorized, "UNAUTHORIZED", exception.Message),
            KeyNotFoundException
                => (HttpStatusCode.NotFound, "NOT_FOUND", exception.Message),
            InvalidOperationException when exception.Message.Contains("conflict", StringComparison.OrdinalIgnoreCase)
                => (HttpStatusCode.Conflict, "CONFLICT", exception.Message),
            _ => (HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An unexpected error occurred.")
        };

        if (statusCode == HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception occurred: {Message}", exception.Message);
        }
        else
        {
            _logger.LogWarning("Handled exception: {ErrorCode} - {Message}", errorCode, exception.Message);
        }

        if (context.Response.HasStarted)
        {
            _logger.LogWarning("Response already started, cannot write error response for {ErrorCode}", errorCode);
            return;
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var response = new ApiErrorResponse(errorCode, clientMessage);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}

// Marker exception for validation errors from services
public class FluentValidationException : Exception
{
    public FluentValidationException(string message) : base(message) { }
}

public class InvalidOtpException : Exception
{
    public InvalidOtpException(string message) : base(message) { }
}

public class OtpExpiredException : Exception
{
    public OtpExpiredException(string message) : base(message) { }
}
