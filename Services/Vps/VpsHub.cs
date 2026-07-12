using Microsoft.AspNetCore.SignalR;

namespace SRXPanel.Services.Vps;

/// <summary>
/// Real-time channel for VPS instances: live stat pushes for the detail page and step-by-step
/// provisioning progress on the order flow. Both surfaces also have a polling fallback.
/// </summary>
public class VpsHub : Hub
{
    public Task JoinVps(int instanceId) => Groups.AddToGroupAsync(Context.ConnectionId, $"vps-{instanceId}");
    public Task LeaveVps(int instanceId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"vps-{instanceId}");
}

public interface IVpsBroadcast
{
    Task StatsAsync(int instanceId, object stats);
    Task StatusAsync(int instanceId, string status);
    Task ProvisionProgressAsync(int instanceId, int percent, string step, string log);
    Task ProvisionCompletedAsync(int instanceId, bool success, string message, string? ip);
}

public class VpsBroadcast : IVpsBroadcast
{
    private readonly IHubContext<VpsHub> _hub;
    public VpsBroadcast(IHubContext<VpsHub> hub) => _hub = hub;

    public Task StatsAsync(int instanceId, object stats) =>
        _hub.Clients.Group($"vps-{instanceId}").SendAsync("stats", stats);

    public Task StatusAsync(int instanceId, string status) =>
        _hub.Clients.Group($"vps-{instanceId}").SendAsync("status", status);

    public Task ProvisionProgressAsync(int instanceId, int percent, string step, string log) =>
        _hub.Clients.Group($"vps-{instanceId}").SendAsync("progress", percent, step, log);

    public Task ProvisionCompletedAsync(int instanceId, bool success, string message, string? ip) =>
        _hub.Clients.Group($"vps-{instanceId}").SendAsync("completed", success, message, ip);
}
