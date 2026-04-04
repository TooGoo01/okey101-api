using System.Text.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Microsoft.EntityFrameworkCore;
using Okey101.Api.Configuration;
using Okey101.Api.Data;
using Okey101.Api.Endpoints;
using Okey101.Api.Hubs;
using Okey101.Api.Middleware;
using Okey101.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// JSON serialization — camelCase, omit nulls
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// API versioning — /api/v1/ prefix
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
});

// Database — EF Core with SQL Server
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Tenant provider — scoped per request
builder.Services.AddScoped<ITenantProvider, TenantProvider>();

// Authentication — JWT
builder.Services.AddJwtAuthentication(builder.Configuration);

// Phone encryption
builder.Services.AddSingleton<IPhoneEncryptionService, PhoneEncryptionService>();

// OTP provider — DevOtpProvider logs OTP to console; swap for real SMS provider later
builder.Services.AddSingleton<IOtpProvider, DevOtpProvider>();

// Auth service
builder.Services.AddScoped<IAuthService, AuthService>();

// Memory cache for rate limiting
builder.Services.AddMemoryCache();

// SignalR for real-time communication
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

// CORS — allow admin panel origin
builder.Services.AddCors(options =>
{
    options.AddPolicy("AdminPanel", policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        if (allowedOrigins is null || allowedOrigins.Length == 0)
        {
            allowedOrigins = builder.Environment.IsDevelopment()
                ? new[] { "http://localhost:5173" }
                : new[] {
                    "https://walvero-okey-admin.azurewebsites.net",
                    "https://walvero-okey-api.azurewebsites.net",
                    "https://gentle-rock-043844c03.azurestaticapps.net",
                    "https://walvero-okey-admin.azurestaticapps.net"
                  };
        }
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Application Insights telemetry
builder.Services.AddApplicationInsightsTelemetry();

// OpenAPI — .NET native
builder.Services.AddOpenApi();

var app = builder.Build();

// Apply pending migrations
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Applying database migrations...");
    await db.Database.MigrateAsync();
    logger.LogInformation("Database migrations applied successfully.");

    // Seed essential data (game center + table) for all environments
    await Okey101.Api.Data.DataSeeder.SeedEssentialDataAsync(app.Services);

    // Seed extra development data only in dev
    if (app.Environment.IsDevelopment())
    {
        await Okey101.Api.Data.DataSeeder.SeedDevelopmentDataAsync(app.Services);
    }
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogError(ex, "Failed to apply database migrations. App will start without migrations.");
}

// Middleware pipeline order matters:
// 1. CORS (outermost — ensures headers are always present, even on errors)
app.UseCors("AdminPanel");

// 2. Exception handler
app.UseMiddleware<ExceptionMiddleware>();

// 3. OpenAPI (dev only)
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// 3. Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

// 4. Tenant resolution (after auth, uses JWT claims)
app.UseMiddleware<TenantMiddleware>();

// 5. Rate limiting (for OTP endpoints)
app.UseMiddleware<RateLimitMiddleware>();

// Dev-only: reset table by clearing all non-completed sessions
if (app.Environment.IsDevelopment())
{
    app.MapPost("/dev/reset-table/{tableId}", async (Guid tableId, AppDbContext db) =>
    {
        // Reactivate table if closed
        var table = await db.Tables
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tableId);
        if (table != null)
        {
            table.Status = Okey101.Api.Models.Enums.TableStatus.Active;
        }

        // Clear all non-terminal sessions
        var sessions = await db.GameSessions
            .IgnoreQueryFilters()
            .Where(gs => gs.TableId == tableId
                && gs.Status != Okey101.Api.Models.Enums.GameSessionStatus.Completed
                && gs.Status != Okey101.Api.Models.Enums.GameSessionStatus.Rejected)
            .ToListAsync();

        foreach (var s in sessions)
        {
            s.Status = Okey101.Api.Models.Enums.GameSessionStatus.Rejected;
            s.EndedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();

        return Results.Ok(new { cleared = sessions.Count, tableReactivated = table != null });
    });
}

// Health check endpoint
var versionSet = app.NewApiVersionSet()
    .HasApiVersion(new ApiVersion(1, 0))
    .Build();

app.MapGet("/api/v1/health", () => Results.Ok(new { status = "healthy" }))
    .WithApiVersionSet(versionSet)
    .MapToApiVersion(new ApiVersion(1, 0));

// Auth endpoints — /api/v1/auth
app.MapGroup("/api/v1/auth")
    .WithApiVersionSet(versionSet)
    .MapToApiVersion(new ApiVersion(1, 0))
    .MapAuthEndpoints();

// Table endpoints — /api/v1/tables
app.MapGroup("/api/v1/tables")
    .WithApiVersionSet(versionSet)
    .MapToApiVersion(new ApiVersion(1, 0))
    .MapTableEndpoints();

// Player endpoints — /api/v1/players
app.MapGroup("/api/v1/players")
    .WithApiVersionSet(versionSet)
    .MapToApiVersion(new ApiVersion(1, 0))
    .MapPlayerEndpoints();

// Game session endpoints — /api/v1/game-sessions
app.MapGroup("/api/v1/game-sessions")
    .WithApiVersionSet(versionSet)
    .MapToApiVersion(new ApiVersion(1, 0))
    .MapGameSessionEndpoints()
    .MapScoreEntryEndpoints();

// Platform admin endpoints — /api/v1/platform
app.MapGroup("/api/v1/platform")
    .WithApiVersionSet(versionSet)
    .MapToApiVersion(new ApiVersion(1, 0))
    .MapPlatformEndpoints();

// Public endpoints — /api/v1/public (no auth required)
app.MapGroup("/api/v1/public")
    .WithApiVersionSet(versionSet)
    .MapToApiVersion(new ApiVersion(1, 0))
    .MapPublicEndpoints();

// SignalR hub — /hubs/game
app.MapHub<GameHub>("/hubs/game");

app.Run();

// Make Program accessible for integration tests
public partial class Program { }
