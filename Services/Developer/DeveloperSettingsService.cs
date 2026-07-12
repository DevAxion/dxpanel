using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Developer;

/// <summary>Every event a webhook endpoint can subscribe to.</summary>
public record WebhookEvent(string Key, string Name, string Description);

public interface IDeveloperSettingsService
{
    Task<DeveloperSettings> GetAsync(string userId);
    Task SaveAsync(string userId, bool debugMode, string? errorReportingEmail);

    IReadOnlyList<WebhookEvent> AvailableEvents { get; }

    Task<List<WebhookEndpoint>> GetWebhooksAsync(string userId);
    Task<WebhookEndpoint> AddWebhookAsync(string userId, string url, bool onDomain, bool onEmail, bool onSsl, bool onInvoice);
    Task UpdateWebhookAsync(string userId, int id, bool onDomain, bool onEmail, bool onSsl, bool onInvoice, bool isActive);
    Task DeleteWebhookAsync(string userId, int id);

    /// <summary>Sends a signed test payload and records the delivery.</summary>
    Task<WebhookDelivery> TestWebhookAsync(string userId, int id);

    Task<List<WebhookDelivery>> GetDeliveriesAsync(string userId, int? endpointId = null, int limit = 25);
}

public class DeveloperSettingsService : IDeveloperSettingsService
{
    private const string ServiceName = "webhook";

    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICommandRunner _runner;
    private readonly ILogger<DeveloperSettingsService> _logger;

    public DeveloperSettingsService(ApplicationDbContext db, IHttpClientFactory httpClientFactory,
        ICommandRunner runner, ILogger<DeveloperSettingsService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _runner = runner;
        _logger = logger;
    }

    public IReadOnlyList<WebhookEvent> AvailableEvents => new[]
    {
        new WebhookEvent("domain_created", "Domain created", "A domain or subdomain was added to the account."),
        new WebhookEvent("domain_deleted", "Domain deleted", "A domain was removed."),
        new WebhookEvent("email_created", "Email account changed", "A mailbox or forwarder was created or removed."),
        new WebhookEvent("ssl_renewed", "SSL renewed / expiring", "A certificate was issued, renewed, or is expiring soon."),
        new WebhookEvent("backup_complete", "Backup complete", "A scheduled or manual backup finished."),
        new WebhookEvent("app_installed", "Application installed", "A one-click application finished installing."),
        new WebhookEvent("invoice_paid", "Invoice paid", "An invoice was paid successfully."),
        new WebhookEvent("deploy_finished", "Deployment finished", "A git deployment succeeded or failed.")
    };

    public async Task<DeveloperSettings> GetAsync(string userId)
    {
        var settings = await _db.DeveloperSettings.FirstOrDefaultAsync(s => s.UserId == userId);
        if (settings != null) return settings;

        settings = new DeveloperSettings { UserId = userId };
        _db.DeveloperSettings.Add(settings);
        await _db.SaveChangesAsync();
        return settings;
    }

    public async Task SaveAsync(string userId, bool debugMode, string? errorReportingEmail)
    {
        var settings = await GetAsync(userId);

        if (!string.IsNullOrWhiteSpace(errorReportingEmail) &&
            !System.Text.RegularExpressions.Regex.IsMatch(errorReportingEmail, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            throw new InvalidOperationException("Enter a valid error-reporting email address.");

        settings.DebugMode = debugMode;
        settings.ErrorReportingEmail = string.IsNullOrWhiteSpace(errorReportingEmail) ? null : errorReportingEmail.Trim();
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public Task<List<WebhookEndpoint>> GetWebhooksAsync(string userId) =>
        _db.WebhookEndpoints.Where(w => w.UserId == userId).OrderByDescending(w => w.CreatedAt).ToListAsync();

    public async Task<WebhookEndpoint> AddWebhookAsync(string userId, string url, bool onDomain, bool onEmail, bool onSsl, bool onInvoice)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new InvalidOperationException("Enter an http:// or https:// webhook URL.");

        if (await _db.WebhookEndpoints.CountAsync(w => w.UserId == userId) >= 10)
            throw new InvalidOperationException("You can register up to 10 webhook endpoints.");

        var endpoint = new WebhookEndpoint
        {
            UserId = userId,
            Url = url.Trim(),
            Secret = "whsec_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant(),
            OnDomainChange = onDomain,
            OnEmailChange = onEmail,
            OnSslExpiring = onSsl,
            OnInvoicePaid = onInvoice,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.WebhookEndpoints.Add(endpoint);
        await _db.SaveChangesAsync();
        return endpoint;
    }

    public async Task UpdateWebhookAsync(string userId, int id, bool onDomain, bool onEmail, bool onSsl, bool onInvoice, bool isActive)
    {
        var endpoint = await _db.WebhookEndpoints.FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId)
            ?? throw new InvalidOperationException("Webhook not found.");

        endpoint.OnDomainChange = onDomain;
        endpoint.OnEmailChange = onEmail;
        endpoint.OnSslExpiring = onSsl;
        endpoint.OnInvoicePaid = onInvoice;
        endpoint.IsActive = isActive;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteWebhookAsync(string userId, int id)
    {
        var endpoint = await _db.WebhookEndpoints.FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId);
        if (endpoint == null) return;

        _db.WebhookEndpoints.Remove(endpoint);
        await _db.SaveChangesAsync();
    }

    public async Task<WebhookDelivery> TestWebhookAsync(string userId, int id)
    {
        var endpoint = await _db.WebhookEndpoints.FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId)
            ?? throw new InvalidOperationException("Webhook not found.");

        var payload = JsonSerializer.Serialize(new
        {
            @event = "webhook_test",
            data = new { message = "This is a test delivery from SRXPanel.", endpointId = endpoint.Id },
            at = DateTime.UtcNow
        });

        var delivery = new WebhookDelivery
        {
            WebhookEndpointId = endpoint.Id,
            EventType = "webhook_test",
            Payload = payload,
            CreatedAt = DateTime.UtcNow
        };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var client = _httpClientFactory.CreateClient("webhook");

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-SRX-Event", "webhook_test");
            request.Headers.Add("X-SRX-Signature", Sign(payload, endpoint.Secret));

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            stopwatch.Stop();

            delivery.ResponseCode = (int)response.StatusCode;
            delivery.ResponseBody = body.Length > 500 ? body[..500] : body;
            delivery.Success = response.IsSuccessStatusCode;
            delivery.DurationMs = stopwatch.ElapsedMilliseconds;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            stopwatch.Stop();
            _logger.LogDebug(ex, "Webhook test to {Url} failed", endpoint.Url);

            delivery.ResponseCode = 0;
            delivery.ResponseBody = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            delivery.Success = false;
            delivery.DurationMs = stopwatch.ElapsedMilliseconds;
        }

        endpoint.LastTriggeredAt = DateTime.UtcNow;
        _db.WebhookDeliveries.Add(delivery);
        await _db.SaveChangesAsync();

        await _runner.LogExternalAsync($"POST {endpoint.Url} (webhook_test)", $"HTTP {delivery.ResponseCode}",
            simulated: false, ServiceName, delivery.Success ? 0 : 1);

        // Keep the delivery log to the most recent 50 per endpoint.
        var stale = await _db.WebhookDeliveries
            .Where(d => d.WebhookEndpointId == endpoint.Id)
            .OrderByDescending(d => d.CreatedAt).Skip(50).ToListAsync();
        if (stale.Count > 0)
        {
            _db.WebhookDeliveries.RemoveRange(stale);
            await _db.SaveChangesAsync();
        }

        return delivery;
    }

    public Task<List<WebhookDelivery>> GetDeliveriesAsync(string userId, int? endpointId = null, int limit = 25) =>
        _db.WebhookDeliveries
            .Include(d => d.Endpoint)
            .Where(d => d.Endpoint!.UserId == userId && (endpointId == null || d.WebhookEndpointId == endpointId))
            .OrderByDescending(d => d.CreatedAt)
            .Take(limit)
            .ToListAsync();

    /// <summary>HMAC-SHA256 over the raw body, so a receiver can verify the payload really came from us.</summary>
    private static string Sign(string payload, string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return "";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}
