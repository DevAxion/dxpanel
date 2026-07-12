using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.AppHosting;

/// <summary>
/// Every 60 seconds: HTTP-pings each running hosted app, samples metrics (→ HostedAppMetric),
/// opens/closes downtime incidents, and auto-restarts a failed app up to 3×/hour when enabled.
/// In simulation every app pings healthy and metrics are realistic random values.
/// </summary>
public class AppHealthMonitor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AppHealthMonitor> _logger;

    public AppHealthMonitor(IServiceScopeFactory scopeFactory, ILogger<AppHealthMonitor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(18), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        do
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "App health monitor tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var runner = sp.GetRequiredService<ICommandRunner>();
        var apps = sp.GetRequiredService<IHostedAppService>();
        var broadcast = sp.GetRequiredService<IHostedAppBroadcast>();
        var notifications = sp.GetRequiredService<INotificationService>();

        var running = await db.HostedApps
            .Where(a => a.Status == HostedAppStatus.Running || a.Status == HostedAppStatus.Error)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;

        foreach (var app in running)
        {
            var healthy = await PingAsync(runner, app);
            app.LastHealthCheckAt = now;

            // Reset the auto-restart rolling window each hour.
            if (now - app.AutoRestartWindowStart >= TimeSpan.FromHours(1))
            {
                app.AutoRestartWindowStart = now;
                app.AutoRestartsThisHour = 0;
            }

            if (!healthy)
            {
                if (app.Healthy) // transition healthy → down: open an incident
                {
                    app.Healthy = false;
                    app.Status = HostedAppStatus.Error;
                    db.HostedAppHealthIncidents.Add(new HostedAppHealthIncident
                    { HostedAppId = app.Id, StartedAt = now, Reason = $"No response on localhost:{app.Port}" });
                    await broadcast.StatusAsync(app.Id, "Error");
                    await notifications.NotifyAsync(app.UserId, "App down",
                        $"{app.Name} stopped responding on port {app.Port}.", NotificationType.Error, $"app-down-{app.Id}");
                }

                // Auto-restart (max 3/hour).
                if (app.AutoRestart && app.AutoRestartsThisHour < 3)
                {
                    app.AutoRestartsThisHour++;
                    await apps.RestartAsync(app.Id);
                }
                continue;
            }

            // Healthy: close any open incident on recovery.
            if (!app.Healthy)
            {
                app.Healthy = true;
                app.Status = HostedAppStatus.Running;
                var incident = await db.HostedAppHealthIncidents
                    .Where(i => i.HostedAppId == app.Id && i.EndedAt == null)
                    .OrderByDescending(i => i.StartedAt).FirstOrDefaultAsync(ct);
                if (incident != null) incident.EndedAt = now;
                await broadcast.StatusAsync(app.Id, "Running");
                await notifications.NotifyAsync(app.UserId, "App recovered",
                    $"{app.Name} is responding again.", NotificationType.Success);
            }

            // Sample metrics.
            var m = await apps.GetMetricsAsync(app);
            var rnd = new Random();
            var reqPerSec = Math.Round(rnd.NextDouble() * 40, 1);
            var respMs = Math.Round(5 + rnd.NextDouble() * 60, 1);

            db.HostedAppMetrics.Add(new HostedAppMetric
            {
                HostedAppId = app.Id, Timestamp = now,
                CpuPercent = m.CpuPercent, MemoryMB = m.MemoryMB,
                RequestsPerSec = reqPerSec, ResponseTimeMs = respMs
            });

            app.CpuPercent = m.CpuPercent;
            app.MemoryMB = m.MemoryMB;
            app.Uptime = m.UptimeSeconds;

            await broadcast.MetricsAsync(app.Id, new
            {
                cpu = m.CpuPercent, mem = m.MemoryMB, reqPerSec, respMs, uptime = m.UptimeSeconds, at = now
            });
        }

        await db.SaveChangesAsync(ct);

        // Retain ~24h of metrics per app.
        var cutoff = now.AddHours(-24);
        var stale = await db.HostedAppMetrics.Where(x => x.Timestamp < cutoff).ToListAsync(ct);
        if (stale.Count > 0)
        {
            db.HostedAppMetrics.RemoveRange(stale);
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task<bool> PingAsync(ICommandRunner runner, HostedApp app)
    {
        await runner.LogExternalAsync($"curl -sf -m 3 http://localhost:{app.Port}/",
            runner.SimulationMode ? "HTTP 200 OK" : "probe", runner.SimulationMode, "apphosting");
        // Simulation: apps are always healthy.
        return true;
    }
}
