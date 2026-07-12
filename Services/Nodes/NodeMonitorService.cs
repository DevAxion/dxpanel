using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Billing;
using SRXPanel.Services.Store;

namespace SRXPanel.Services.Nodes;

/// <summary>
/// Pings every node once a minute, stores a metrics sample, evaluates alert thresholds and
/// escalates unacknowledged critical alerts (email after 15 min, SMS after 30). In simulation
/// the SSH layer returns realistic random metrics so the whole pipeline exercises end-to-end.
/// </summary>
public class NodeMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NodeMonitorService> _logger;

    // A sustained condition tracker keyed by "nodeId:type" — CPU must stay high for 5 min to alert.
    private readonly Dictionary<string, DateTime> _sustainedSince = new();

    public NodeMonitorService(IServiceScopeFactory scopeFactory, ILogger<NodeMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        do
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Node monitor tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var ssh = sp.GetRequiredService<INodeSshService>();
        var broadcast = sp.GetRequiredService<INodeBroadcast>();

        var nodes = await db.ServerNodes.Include(n => n.Services).Where(n => n.IsActive).ToListAsync(ct);

        foreach (var node in nodes)
        {
            var (reachable, latency) = await ssh.TestConnectionAsync(node);
            node.LastPingAt = DateTime.UtcNow;
            node.LatencyMs = reachable ? latency : null;

            if (!reachable)
            {
                if (node.Status != NodeStatus.Maintenance) node.Status = NodeStatus.Offline;
                await RaiseAlertAsync(db, broadcast, node, NodeAlertType.Unreachable, AlertSeverity.Critical,
                    $"{node.Name} ({node.IpAddress}) is unreachable over SSH.");
                await broadcast.StatusAsync(node.Id, "Offline");
                continue;
            }

            if (node.Status == NodeStatus.Offline) node.Status = NodeStatus.Online;

            var metrics = await ssh.GetMetricsAsync(node);

            var sample = new ServerMetric
            {
                NodeId = node.Id,
                Timestamp = DateTime.UtcNow,
                CpuPercent = metrics.CpuPercent,
                RamPercent = metrics.RamPercent,
                DiskPercent = metrics.DiskPercent,
                NetworkInMbps = metrics.NetworkInMbps,
                NetworkOutMbps = metrics.NetworkOutMbps,
                LoadAverage1 = metrics.Load1,
                LoadAverage5 = metrics.Load5,
                LoadAverage15 = metrics.Load15,
                ActiveConnections = metrics.ActiveConnections
            };
            db.ServerMetrics.Add(sample);

            await broadcast.MetricsAsync(node.Id, new
            {
                cpu = metrics.CpuPercent, ram = metrics.RamPercent, disk = metrics.DiskPercent,
                netIn = metrics.NetworkInMbps, netOut = metrics.NetworkOutMbps,
                load1 = metrics.Load1, conns = metrics.ActiveConnections, at = DateTime.UtcNow
            });

            await EvaluateThresholdsAsync(db, broadcast, node, metrics, ct);
            await CheckServicesAsync(db, broadcast, ssh, node, ct);
        }

        await db.SaveChangesAsync(ct);

        // Retain ~24h of samples per node (1440 minutes).
        var cutoff = DateTime.UtcNow.AddHours(-24);
        var stale = await db.ServerMetrics.Where(m => m.Timestamp < cutoff).ToListAsync(ct);
        if (stale.Count > 0)
        {
            db.ServerMetrics.RemoveRange(stale);
            await db.SaveChangesAsync(ct);
        }

        await EscalateAsync(sp, db, ct);
    }

    private async Task EvaluateThresholdsAsync(ApplicationDbContext db, INodeBroadcast broadcast,
        ServerNode node, NodeMetrics metrics, CancellationToken ct)
    {
        // CPU must stay above threshold for 5 minutes to raise a critical alert.
        await SustainedAsync(db, broadcast, node, NodeAlertType.CpuHigh, AlertSeverity.Critical,
            metrics.CpuPercent > node.CpuThreshold, TimeSpan.FromMinutes(5),
            $"{node.Name} CPU has been above {node.CpuThreshold}% for 5 minutes (now {metrics.CpuPercent:0}%).");

        if (metrics.RamPercent > node.RamThreshold)
            await RaiseAlertAsync(db, broadcast, node, NodeAlertType.RamHigh, AlertSeverity.Warning,
                $"{node.Name} RAM is at {metrics.RamPercent:0}% (threshold {node.RamThreshold}%).");

        if (metrics.DiskPercent > node.DiskThreshold)
            await RaiseAlertAsync(db, broadcast, node, NodeAlertType.DiskHigh, AlertSeverity.Critical,
                $"{node.Name} disk is at {metrics.DiskPercent:0}% (threshold {node.DiskThreshold}%).");
    }

    private async Task SustainedAsync(ApplicationDbContext db, INodeBroadcast broadcast, ServerNode node,
        NodeAlertType type, AlertSeverity severity, bool conditionMet, TimeSpan window, string message)
    {
        var key = $"{node.Id}:{type}";
        if (!conditionMet)
        {
            _sustainedSince.Remove(key);
            return;
        }

        if (!_sustainedSince.TryGetValue(key, out var since))
        {
            _sustainedSince[key] = DateTime.UtcNow;
            return;
        }

        if (DateTime.UtcNow - since >= window)
            await RaiseAlertAsync(db, broadcast, node, type, severity, message);
    }

    private async Task CheckServicesAsync(ApplicationDbContext db, INodeBroadcast broadcast,
        INodeSshService ssh, ServerNode node, CancellationToken ct)
    {
        foreach (var service in node.Services)
        {
            var status = await ssh.GetServiceStatusAsync(node, service.ServiceType);
            service.Status = status;
            service.LastCheckedAt = DateTime.UtcNow;

            if (status is ServerServiceStatus.Stopped or ServerServiceStatus.Error)
                await RaiseAlertAsync(db, broadcast, node, NodeAlertType.ServiceDown, AlertSeverity.Warning,
                    $"{node.Name}: {service.ServiceType} is {status}.");
        }
    }

    /// <summary>Adds an alert unless an identical unacknowledged one already exists (dedupe).</summary>
    private static async Task RaiseAlertAsync(ApplicationDbContext db, INodeBroadcast broadcast,
        ServerNode node, NodeAlertType type, AlertSeverity severity, string message)
    {
        var dedupeKey = $"{node.Id}:{type}";
        var exists = await db.NodeAlerts.AnyAsync(a => a.DedupeKey == dedupeKey && !a.IsAcknowledged);
        if (exists) return;

        var alert = new NodeAlert
        {
            NodeId = node.Id, Type = type, Severity = severity, Message = message,
            DedupeKey = dedupeKey, CreatedAt = DateTime.UtcNow
        };
        db.NodeAlerts.Add(alert);

        await broadcast.AlertAsync(new { nodeId = node.Id, node = node.Name, severity = severity.ToString(), message });
    }

    /// <summary>Emails SuperAdmins for unacknowledged critical alerts &gt;15 min old; SMS after 30 min.</summary>
    private static async Task EscalateAsync(IServiceProvider sp, ApplicationDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var pending = await db.NodeAlerts.Include(a => a.Node)
            .Where(a => !a.IsAcknowledged && a.Severity == AlertSeverity.Critical && !a.Escalated && a.CreatedAt < now.AddMinutes(-15))
            .ToListAsync(ct);
        if (pending.Count == 0) return;

        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var mailer = sp.GetRequiredService<IMailerService>();
        var sms = sp.GetRequiredService<ISmsSender>();
        var notifications = sp.GetRequiredService<INotificationService>();

        var admins = await userManager.GetUsersInRoleAsync(Roles.SuperAdmin);

        foreach (var alert in pending)
        {
            foreach (var admin in admins)
            {
                if (!string.IsNullOrEmpty(admin.Email))
                    await mailer.SendTemplateAsync(admin.Email, $"[CRITICAL] {alert.Node?.Name}", "node_alert",
                        new Dictionary<string, string>
                        {
                            ["NODE"] = alert.Node?.Name ?? "node",
                            ["SEVERITY"] = alert.Severity.ToString(),
                            ["MESSAGE"] = alert.Message,
                            ["RAISED_AT"] = alert.CreatedAt.ToString("u")
                        });

                await notifications.NotifyAsync(admin.Id, $"Critical: {alert.Node?.Name}", alert.Message,
                    NotificationType.Error, $"node-alert-{alert.Id}");

                // SMS only once the alert is >30 minutes old.
                if (alert.CreatedAt < now.AddMinutes(-30) && !string.IsNullOrEmpty(admin.PhoneNumber))
                    await sms.SendAsync(admin.PhoneNumber, $"SRXPanel CRITICAL: {alert.Message}");
            }

            alert.Escalated = true;
        }

        await db.SaveChangesAsync(ct);
    }
}
