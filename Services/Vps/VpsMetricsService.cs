using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;

namespace SRXPanel.Services.Vps;

/// <summary>
/// Every 5 minutes: samples stats for each running VPS (→ VpsMetric), accounts monthly bandwidth
/// with 80/95/100% alerts (auto-suspend on overage), fires expiry reminders, and trims old samples.
/// In simulation the Proxmox layer returns realistic random stats so the pipeline exercises fully.
/// </summary>
public class VpsMetricsService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VpsMetricsService> _logger;

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    public VpsMetricsService(IServiceScopeFactory scopeFactory, ILogger<VpsMetricsService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "VPS metrics tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var proxmox = sp.GetRequiredService<IProxmoxService>();
        var broadcast = sp.GetRequiredService<IVpsBroadcast>();
        var notifications = sp.GetRequiredService<INotificationService>();
        var manager = sp.GetRequiredService<IVpsManagerService>();
        var sms = sp.GetRequiredService<SRXPanel.Services.Store.ISmsSender>();

        var instances = await db.VpsInstances.Include(v => v.Node)
            .Where(v => v.Status == VpsStatus.Running)
            .ToListAsync(ct);

        foreach (var vps in instances)
        {
            if (vps.Node == null) continue;

            var stats = await proxmox.GetVmStatsAsync(vps.Node, vps.VmId);

            db.VpsMetrics.Add(new VpsMetric
            {
                VpsInstanceId = vps.Id,
                Timestamp = DateTime.UtcNow,
                CpuPercent = stats.CpuPercent,
                RamPercent = stats.RamPercent,
                DiskPercent = stats.DiskPercent,
                NetworkInMbps = stats.NetworkInMbps,
                NetworkOutMbps = stats.NetworkOutMbps,
                DiskReadMbps = stats.DiskReadMbps,
                DiskWriteMbps = stats.DiskWriteMbps
            });

            // Bandwidth accounting: GB transferred this interval = Mbps × seconds ÷ 8 ÷ 1000.
            var gbThisTick = (stats.NetworkInMbps + stats.NetworkOutMbps) * Interval.TotalSeconds / 8.0 / 1000.0;
            vps.BandwidthUsed = Math.Round(vps.BandwidthUsed + gbThisTick, 3);

            await broadcast.StatsAsync(vps.Id, new
            {
                cpu = stats.CpuPercent, ram = stats.RamPercent, disk = stats.DiskPercent,
                netIn = stats.NetworkInMbps, netOut = stats.NetworkOutMbps,
                bwUsed = vps.BandwidthUsed, bwPct = vps.BandwidthPercent, at = DateTime.UtcNow
            });

            await CheckBandwidthAsync(db, notifications, manager, sms, vps, ct);
        }

        await db.SaveChangesAsync(ct);

        await CheckExpiryAsync(db, notifications, ct);

        // Keep ~7 days of samples per instance.
        var cutoff = DateTime.UtcNow.AddDays(-7);
        var stale = await db.VpsMetrics.Where(m => m.Timestamp < cutoff).ToListAsync(ct);
        if (stale.Count > 0)
        {
            db.VpsMetrics.RemoveRange(stale);
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task CheckBandwidthAsync(ApplicationDbContext db, INotificationService notifications,
        IVpsManagerService manager, SRXPanel.Services.Store.ISmsSender sms, VpsInstance vps, CancellationToken ct)
    {
        // Reset the counter at the start of a new monthly cycle.
        if (DateTime.UtcNow - vps.BandwidthCycleStart >= TimeSpan.FromDays(30))
        {
            vps.BandwidthUsed = 0;
            vps.BandwidthCycleStart = DateTime.UtcNow;
            if (vps.BandwidthSuspended) await manager.ResumeAsync(vps, "system");
            return;
        }

        if (vps.BandwidthGB <= 0 || !vps.NotifyBandwidth) return;
        var pct = vps.BandwidthPercent;
        var cycle = vps.BandwidthCycleStart.ToString("yyyyMM");

        if (pct >= 100)
        {
            await notifications.NotifyAsync(vps.UserId, "VPS bandwidth exhausted",
                $"{vps.Hostname} has used 100% of its {vps.BandwidthGB} GB allowance and has been suspended.",
                NotificationType.Error, $"vps-bw-100-{vps.Id}-{cycle}");
            if (!vps.BandwidthSuspended && vps.Status == VpsStatus.Running)
            {
                await manager.SuspendAsync(vps, "system", bandwidth: true);
                // SMS alert to the owner if a phone number is on file.
                var phone = await db.Users.Where(u => u.Id == vps.UserId).Select(u => u.PhoneNumber).FirstOrDefaultAsync(ct);
                await sms.SendAsync(phone,
                    $"SRXPanel: {vps.Hostname} reached 100% of its bandwidth and has been suspended.");
            }
        }
        else if (pct >= 95)
        {
            await notifications.NotifyAsync(vps.UserId, "VPS bandwidth at 95%",
                $"{vps.Hostname} has used {pct}% of its {vps.BandwidthGB} GB bandwidth.",
                NotificationType.Warning, $"vps-bw-95-{vps.Id}-{cycle}");
        }
        else if (pct >= 80)
        {
            await notifications.NotifyAsync(vps.UserId, "VPS bandwidth at 80%",
                $"{vps.Hostname} has used {pct}% of its {vps.BandwidthGB} GB bandwidth.",
                NotificationType.Warning, $"vps-bw-80-{vps.Id}-{cycle}");
        }
    }

    private static async Task CheckExpiryAsync(ApplicationDbContext db, INotificationService notifications, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var soon = await db.VpsInstances
            .Where(v => v.Status != VpsStatus.Deleted && v.ExpiresAt != null && v.ExpiresAt > now && v.ExpiresAt <= now.AddDays(7))
            .ToListAsync(ct);

        foreach (var vps in soon)
        {
            var days = (int)Math.Ceiling((vps.ExpiresAt!.Value - now).TotalDays);
            if (days is 7 or 1)
                await notifications.NotifyAsync(vps.UserId, "VPS expiring soon",
                    $"{vps.Hostname} expires in {days} day(s). Renew to avoid interruption.",
                    NotificationType.Warning, $"vps-expiry-{vps.Id}-{days}");
        }
    }
}
