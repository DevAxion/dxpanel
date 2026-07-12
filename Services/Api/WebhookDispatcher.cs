using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Api;

/// <summary>
/// Delivers outbound webhooks to a client's configured endpoints. Simulation-safe:
/// the HTTP POST is logged via ICommandRunner rather than actually sent on dev.
/// </summary>
public interface IWebhookDispatcher
{
    Task DispatchAsync(string userId, string eventType, object payload);
}

public class WebhookDispatcher : IWebhookDispatcher
{
    private readonly ApplicationDbContext _db;
    private readonly ICommandRunner _log;

    public WebhookDispatcher(ApplicationDbContext db, ICommandRunner log)
    {
        _db = db;
        _log = log;
    }

    public async Task DispatchAsync(string userId, string eventType, object payload)
    {
        var endpoints = await _db.WebhookEndpoints
            .Where(w => w.UserId == userId && w.IsActive)
            .ToListAsync();

        var body = JsonSerializer.Serialize(new { @event = eventType, data = payload, at = DateTime.UtcNow });

        foreach (var ep in endpoints)
        {
            if (!Subscribed(ep, eventType)) continue;
            await _log.LogExternalAsync($"POST {ep.Url} ({eventType})", body, true, "webhook");
            ep.LastTriggeredAt = DateTime.UtcNow;
        }
        if (endpoints.Count > 0) await _db.SaveChangesAsync();
    }

    private static bool Subscribed(Models.WebhookEndpoint ep, string eventType) => eventType switch
    {
        var e when e.StartsWith("domain") => ep.OnDomainChange,
        var e when e.StartsWith("email") => ep.OnEmailChange,
        var e when e.StartsWith("ssl") => ep.OnSslExpiring,
        var e when e.StartsWith("invoice") => ep.OnInvoicePaid,
        _ => false
    };
}
