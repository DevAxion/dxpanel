using Microsoft.AspNetCore.SignalR;

namespace SRXPanel.Services.Developer;

/// <summary>
/// Real-time channel for the developer tools: package-manager output, git deployment
/// logs and live log tailing. Every page that uses it also has a polling fallback,
/// so the tools still work when WebSockets are unavailable.
/// </summary>
public class DevToolsHub : Hub
{
    public Task JoinDeployment(int deploymentId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, $"deploy-{deploymentId}");

    public Task JoinRunner(string runnerId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, $"runner-{runnerId}");

    public Task JoinLogTail(string tailId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, $"tail-{tailId}");

    public Task LeaveLogTail(string tailId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, $"tail-{tailId}");
}

public interface IDevToolsBroadcast
{
    Task DeployOutputAsync(int deploymentId, string line);
    Task DeployCompletedAsync(int deploymentId, bool success, string message);

    Task RunnerOutputAsync(string runnerId, string line);
    Task RunnerCompletedAsync(string runnerId, int exitCode);

    Task LogLinesAsync(string tailId, IEnumerable<string> lines);
}

public class DevToolsBroadcast : IDevToolsBroadcast
{
    private readonly IHubContext<DevToolsHub> _hub;
    public DevToolsBroadcast(IHubContext<DevToolsHub> hub) => _hub = hub;

    public Task DeployOutputAsync(int deploymentId, string line) =>
        _hub.Clients.Group($"deploy-{deploymentId}").SendAsync("deployOutput", line);

    public Task DeployCompletedAsync(int deploymentId, bool success, string message) =>
        _hub.Clients.Group($"deploy-{deploymentId}").SendAsync("deployCompleted", success, message);

    public Task RunnerOutputAsync(string runnerId, string line) =>
        _hub.Clients.Group($"runner-{runnerId}").SendAsync("runnerOutput", line);

    public Task RunnerCompletedAsync(string runnerId, int exitCode) =>
        _hub.Clients.Group($"runner-{runnerId}").SendAsync("runnerCompleted", exitCode);

    public Task LogLinesAsync(string tailId, IEnumerable<string> lines) =>
        _hub.Clients.Group($"tail-{tailId}").SendAsync("logLines", lines);
}
