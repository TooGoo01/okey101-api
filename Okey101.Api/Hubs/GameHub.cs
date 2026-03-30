using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Okey101.Api.Hubs;

[Authorize]
public class GameHub : Hub
{
    private readonly ILogger<GameHub> _logger;

    public GameHub(ILogger<GameHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();

        var sessionId = httpContext?.Request.Query["sessionId"].FirstOrDefault();
        if (!string.IsNullOrEmpty(sessionId) && Guid.TryParse(sessionId, out _))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"session_{sessionId}");
        }

        var gameCenterId = httpContext?.Request.Query["gameCenterId"].FirstOrDefault();
        if (!string.IsNullOrEmpty(gameCenterId) && Guid.TryParse(gameCenterId, out _))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"gamecenter_{gameCenterId}");
        }

        _logger.LogInformation("SignalR client connected: {ConnectionId}, Session: {SessionId}, GameCenter: {GameCenterId}",
            Context.ConnectionId, sessionId, gameCenterId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception is not null)
        {
            _logger.LogWarning(exception, "SignalR client disconnected with error: {ConnectionId}", Context.ConnectionId);
        }
        else
        {
            _logger.LogInformation("SignalR client disconnected: {ConnectionId}", Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}
