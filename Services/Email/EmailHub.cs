using Microsoft.AspNetCore.SignalR;

namespace SRXPanel.Services.Email;

/// <summary>
/// Real-time channel for the mail queue: live status-count pushes for the client queue page.
/// A polling fallback covers browsers without SignalR.
/// </summary>
public class EmailHub : Hub
{
    public Task JoinUser(string userId) => Groups.AddToGroupAsync(Context.ConnectionId, $"mail-{userId}");
    public Task JoinAdmin() => Groups.AddToGroupAsync(Context.ConnectionId, "mail-admin");
}

public interface IEmailBroadcast
{
    Task QueueUpdatedAsync(string userId, object counts);
    Task AdminQueueUpdatedAsync(object counts);
}

public class EmailBroadcast : IEmailBroadcast
{
    private readonly IHubContext<EmailHub> _hub;
    public EmailBroadcast(IHubContext<EmailHub> hub) => _hub = hub;

    public Task QueueUpdatedAsync(string userId, object counts) =>
        _hub.Clients.Group($"mail-{userId}").SendAsync("queue", counts);

    public Task AdminQueueUpdatedAsync(object counts) =>
        _hub.Clients.Group("mail-admin").SendAsync("queue", counts);
}
