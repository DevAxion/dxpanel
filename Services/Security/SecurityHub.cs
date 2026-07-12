using Microsoft.AspNetCore.SignalR;

namespace SRXPanel.Services.Security;

/// <summary>
/// Real-time channel for security events: brute-force attempt feed, antivirus/malware
/// scan progress and new WAF alerts. Pages subscribe; services broadcast via IHubContext.
/// Fully optional — pages also work with a normal page load if SignalR is unavailable.
/// </summary>
public class SecurityHub : Hub
{
    // Client-invoked: join the per-user scan progress group.
    public Task JoinUser(string userId) => Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");

    // Client-invoked: join the admin live security feed.
    public Task JoinAdminFeed() => Groups.AddToGroupAsync(Context.ConnectionId, "admin-feed");
}

public interface ISecurityBroadcast
{
    Task ScanProgressAsync(string userId, int percent, string currentFile);
    Task ScanCompleteAsync(string userId, int scanned, int threats);
    Task AttemptAsync(object attempt);
    Task AlertAsync(object alert);
}

public class SecurityBroadcast : ISecurityBroadcast
{
    private readonly IHubContext<SecurityHub> _hub;
    public SecurityBroadcast(IHubContext<SecurityHub> hub) => _hub = hub;

    public Task ScanProgressAsync(string userId, int percent, string currentFile) =>
        _hub.Clients.Group($"user-{userId}").SendAsync("scanProgress", percent, currentFile);

    public Task ScanCompleteAsync(string userId, int scanned, int threats) =>
        _hub.Clients.Group($"user-{userId}").SendAsync("scanComplete", scanned, threats);

    public Task AttemptAsync(object attempt) => _hub.Clients.Group("admin-feed").SendAsync("attempt", attempt);
    public Task AlertAsync(object alert) => _hub.Clients.Group("admin-feed").SendAsync("alert", alert);
}
