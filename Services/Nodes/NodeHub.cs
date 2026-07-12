using Microsoft.AspNetCore.SignalR;

namespace SRXPanel.Services.Nodes;

/// <summary>
/// Real-time channel for the node fleet: live metric pushes for the node detail page and
/// step-by-step domain migration progress. Both surfaces also have a polling fallback.
/// </summary>
public class NodeHub : Hub
{
    public Task JoinNode(int nodeId) => Groups.AddToGroupAsync(Context.ConnectionId, $"node-{nodeId}");
    public Task LeaveNode(int nodeId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"node-{nodeId}");
    public Task JoinMigration(int migrationId) => Groups.AddToGroupAsync(Context.ConnectionId, $"migration-{migrationId}");
    public Task JoinFleet() => Groups.AddToGroupAsync(Context.ConnectionId, "fleet");
}

public interface INodeBroadcast
{
    Task MetricsAsync(int nodeId, object metrics);
    Task StatusAsync(int nodeId, string status);
    Task AlertAsync(object alert);
    Task MigrationProgressAsync(int migrationId, int percent, string step, string log);
    Task MigrationCompletedAsync(int migrationId, bool success, string message);
}

public class NodeBroadcast : INodeBroadcast
{
    private readonly IHubContext<NodeHub> _hub;
    public NodeBroadcast(IHubContext<NodeHub> hub) => _hub = hub;

    public Task MetricsAsync(int nodeId, object metrics) =>
        _hub.Clients.Group($"node-{nodeId}").SendAsync("metrics", metrics);

    public Task StatusAsync(int nodeId, string status) =>
        _hub.Clients.Group("fleet").SendAsync("nodeStatus", nodeId, status);

    public Task AlertAsync(object alert) =>
        _hub.Clients.Group("fleet").SendAsync("alert", alert);

    public Task MigrationProgressAsync(int migrationId, int percent, string step, string log) =>
        _hub.Clients.Group($"migration-{migrationId}").SendAsync("progress", percent, step, log);

    public Task MigrationCompletedAsync(int migrationId, bool success, string message) =>
        _hub.Clients.Group($"migration-{migrationId}").SendAsync("completed", success, message);
}
