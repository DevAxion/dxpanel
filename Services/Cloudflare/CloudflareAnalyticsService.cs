using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;

namespace SRXPanel.Services.Cloudflare;

/// <summary>
/// Persists a daily analytics snapshot per linked zone and raises threat alerts.
/// Runs hourly; the snapshot for "today" is upserted so charts stay current, and rows
/// older than 90 days are trimmed. In simulation the gateway returns deterministic data.
/// </summary>
public class CloudflareAnalyticsService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CloudflareAnalyticsService> _logger;

    public CloudflareAnalyticsService(IServiceScopeFactory scopeFactory, ILogger<CloudflareAnalyticsService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let migrations + seeding finish first.
        await Task.Delay(TimeSpan.FromSeconds(25), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await SnapshotAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Cloudflare analytics snapshot failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SnapshotAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cf = scope.ServiceProvider.GetRequiredService<ICloudflareService>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var zones = await db.CloudflareDomains
            .Include(z => z.Account).Include(z => z.Domain)
            .Where(z => z.Status == CloudflareZoneStatus.Active)
            .ToListAsync(ct);

        var from = DateTime.UtcNow.Date.AddDays(-6);
        var to = DateTime.UtcNow.Date;

        foreach (var zone in zones)
        {
            var points = await cf.GetAnalyticsAsync(zone.Account!, zone.ZoneId, from, to);

            foreach (var point in points)
            {
                var existing = await db.CloudflareAnalytics
                    .FirstOrDefaultAsync(a => a.CloudflareDomainId == zone.Id && a.Date == point.Date, ct);

                if (existing == null)
                {
                    db.CloudflareAnalytics.Add(new CloudflareAnalytics
                    {
                        CloudflareDomainId = zone.Id,
                        Date = point.Date,
                        Requests = point.Requests,
                        CachedRequests = point.CachedRequests,
                        Bandwidth = point.Bandwidth,
                        CachedBandwidth = point.CachedBandwidth,
                        Threats = point.Threats,
                        PageViews = point.PageViews,
                        UniqueVisitors = point.UniqueVisitors,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    existing.Requests = point.Requests;
                    existing.CachedRequests = point.CachedRequests;
                    existing.Bandwidth = point.Bandwidth;
                    existing.CachedBandwidth = point.CachedBandwidth;
                    existing.Threats = point.Threats;
                    existing.PageViews = point.PageViews;
                    existing.UniqueVisitors = point.UniqueVisitors;
                }
            }

            // High-threat alert: today's projected hourly rate over the threshold.
            var today = points.FirstOrDefault(p => p.Date == to);
            if (today != null)
            {
                var perHour = today.Threats / Math.Max(1, DateTime.UtcNow.Hour + 1);
                if (perHour > 1000)
                    await notifications.NotifyAsync(zone.Account!.UserId, "High threat volume",
                        $"{zone.Domain?.DomainName}: ~{perHour:N0} threats/hour blocked by Cloudflare today.",
                        NotificationType.Warning, $"cf-threats-{zone.Id}-{to:yyyyMMdd}");
            }
        }

        await db.SaveChangesAsync(ct);

        // Trim history beyond 90 days.
        var cutoff = DateTime.UtcNow.Date.AddDays(-90);
        var stale = await db.CloudflareAnalytics.Where(a => a.Date < cutoff).ToListAsync(ct);
        if (stale.Count > 0)
        {
            db.CloudflareAnalytics.RemoveRange(stale);
            await db.SaveChangesAsync(ct);
        }
    }
}
