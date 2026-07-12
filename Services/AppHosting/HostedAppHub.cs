using Microsoft.AspNetCore.SignalR;

namespace SRXPanel.Services.AppHosting;

/// <summary>
/// Real-time channel for hosted apps: live metric pushes and log tailing for the detail page,
/// plus deploy/install progress on the create + deploy flows. All surfaces have a poll fallback.
/// </summary>
public class HostedAppHub : Hub
{
    public Task JoinApp(int appId) => Groups.AddToGroupAsync(Context.ConnectionId, $"app-{appId}");
    public Task LeaveApp(int appId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"app-{appId}");
}

public interface IHostedAppBroadcast
{
    Task MetricsAsync(int appId, object metrics);
    Task StatusAsync(int appId, string status);
    Task LogAsync(int appId, string type, string line);
    Task DeployProgressAsync(int appId, int percent, string step, string log);
    Task DeployCompletedAsync(int appId, bool success, string message);
}

public class HostedAppBroadcast : IHostedAppBroadcast
{
    private readonly IHubContext<HostedAppHub> _hub;
    public HostedAppBroadcast(IHubContext<HostedAppHub> hub) => _hub = hub;

    public Task MetricsAsync(int appId, object metrics) =>
        _hub.Clients.Group($"app-{appId}").SendAsync("metrics", metrics);

    public Task StatusAsync(int appId, string status) =>
        _hub.Clients.Group($"app-{appId}").SendAsync("status", status);

    public Task LogAsync(int appId, string type, string line) =>
        _hub.Clients.Group($"app-{appId}").SendAsync("log", type, line);

    public Task DeployProgressAsync(int appId, int percent, string step, string log) =>
        _hub.Clients.Group($"app-{appId}").SendAsync("progress", percent, step, log);

    public Task DeployCompletedAsync(int appId, bool success, string message) =>
        _hub.Clients.Group($"app-{appId}").SendAsync("deployCompleted", success, message);
}
