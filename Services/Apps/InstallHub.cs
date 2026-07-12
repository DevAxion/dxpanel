using Microsoft.AspNetCore.SignalR;

namespace SRXPanel.Services.Apps;

/// <summary>
/// Real-time channel for application install/update/uninstall jobs. The progress page
/// joins a per-job group; the installer broadcasts progress and log lines.
/// Degrades gracefully — the progress page also polls the job as a fallback.
/// </summary>
public class InstallHub : Hub
{
    public Task JoinJob(int jobId) => Groups.AddToGroupAsync(Context.ConnectionId, $"job-{jobId}");
}

public interface IInstallBroadcast
{
    Task ProgressAsync(int jobId, int percent, string step, string logLine);
    Task CompletedAsync(int jobId, bool success, string message, int? installationId);
}

public class InstallBroadcast : IInstallBroadcast
{
    private readonly IHubContext<InstallHub> _hub;
    public InstallBroadcast(IHubContext<InstallHub> hub) => _hub = hub;

    public Task ProgressAsync(int jobId, int percent, string step, string logLine) =>
        _hub.Clients.Group($"job-{jobId}").SendAsync("progress", percent, step, logLine);

    public Task CompletedAsync(int jobId, bool success, string message, int? installationId) =>
        _hub.Clients.Group($"job-{jobId}").SendAsync("completed", success, message, installationId);
}
