using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Nodes;

namespace SRXPanel.Pages.Admin.Nodes;

public class DetailModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly INodeManagerService _nodes;
    private readonly INodeSshService _ssh;
    private readonly IAuditLogService _auditLog;

    public DetailModel(ApplicationDbContext db, INodeManagerService nodes, INodeSshService ssh, IAuditLogService auditLog)
    {
        _db = db;
        _nodes = nodes;
        _ssh = ssh;
        _auditLog = auditLog;
    }

    [BindProperty(SupportsGet = true)] public int Id { get; set; }
    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "overview";

    public ServerNode Node { get; private set; } = null!;
    public ServerMetric? Latest { get; private set; }
    public List<ServerMetric> History { get; private set; } = new();
    public List<ProcessInfo> Processes { get; private set; } = new();
    public List<DiskUsage> Disks { get; private set; } = new();
    public NodeMetrics? LiveMetrics { get; private set; }

    // Tab data
    public List<DomainNode> Domains { get; private set; } = new();
    public List<UserNode> Users { get; private set; } = new();
    public List<NodeAlert> Alerts { get; private set; } = new();
    public List<ServerNode> OtherNodes { get; private set; } = new();

    public string AgentInstallCommand =>
        $"curl -fsSL https://{Request.Host}/scripts/agent/srxpanel-agent.sh | sudo bash -s -- --panel {Request.Host} --node {Node.Id}";

    private async Task<bool> LoadAsync()
    {
        var node = await _nodes.GetNodeAsync(Id);
        if (node == null) return false;
        Node = node;
        Latest = await _nodes.GetLatestMetricAsync(Id);
        return true;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync()) return NotFound();

        switch (Tab)
        {
            case "services":
                break;
            case "domains":
                Domains = await _db.DomainNodes.Include(d => d.Domain).Where(d => d.NodeId == Id).ToListAsync();
                OtherNodes = await _db.ServerNodes.Where(n => n.Id != Id && n.IsActive).ToListAsync();
                break;
            case "users":
                Users = await _db.UserNodes.Include(u => u.User).Where(u => u.NodeId == Id).ToListAsync();
                OtherNodes = await _db.ServerNodes.Where(n => n.Id != Id && n.IsActive).ToListAsync();
                break;
            case "alerts":
                Alerts = await _db.NodeAlerts.Where(a => a.NodeId == Id)
                    .OrderByDescending(a => a.CreatedAt).Take(50).ToListAsync();
                break;
            case "maintenance":
                break;
            default: // overview
                LiveMetrics = await _ssh.GetMetricsAsync(Node);
                Processes = await _ssh.GetProcessListAsync(Node);
                Disks = await _ssh.GetDiskUsageAsync(Node);
                History = await _nodes.GetMetricHistoryAsync(Id, 1);
                break;
        }

        return Page();
    }

    /// <summary>SignalR fallback: current metrics as JSON.</summary>
    public async Task<IActionResult> OnGetMetricsAsync()
    {
        if (!await LoadAsync()) return NotFound();
        var m = await _ssh.GetMetricsAsync(Node);
        return new JsonResult(new { cpu = m.CpuPercent, ram = m.RamPercent, disk = m.DiskPercent, netIn = m.NetworkInMbps, netOut = m.NetworkOutMbps, load1 = m.Load1, conns = m.ActiveConnections });
    }

    // ---------------- Services ----------------

    public async Task<IActionResult> OnPostServiceActionAsync(ServerServiceType service, string action)
    {
        if (!await LoadAsync()) return NotFound();
        var result = await _ssh.ServiceActionAsync(Node, service, action);

        var svc = await _db.ServerServices.FirstOrDefaultAsync(s => s.NodeId == Id && s.ServiceType == service);
        if (svc != null)
        {
            svc.Status = action == "stop" ? ServerServiceStatus.Stopped : ServerServiceStatus.Running;
            svc.LastCheckedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        await _auditLog.LogAsync(action, "ServerService", $"{Id}", $"{service} on {Node.Name}");
        TempData[result.Success ? "Success" : "Error"] = result.Success
            ? $"{service} {action} issued{(result.Simulated ? " (simulated)" : "")}."
            : $"Failed to {action} {service}: {result.Output}";
        return RedirectToTab("services");
    }

    public async Task<IActionResult> OnGetServiceLogAsync(ServerServiceType service)
    {
        if (!await LoadAsync()) return NotFound();
        var log = await _ssh.GetServiceLogAsync(Node, service, 50);
        return new JsonResult(new { log });
    }

    public async Task<IActionResult> OnPostInstallServiceAsync(ServerServiceType service)
    {
        if (!await LoadAsync()) return NotFound();
        await _ssh.ExecuteCommandAsync(Node, $"apt-get install -y {InstallPackage(service)}");

        if (!await _db.ServerServices.AnyAsync(s => s.NodeId == Id && s.ServiceType == service))
        {
            _db.ServerServices.Add(new ServerService { NodeId = Id, ServiceType = service, Status = ServerServiceStatus.Running, LastCheckedAt = DateTime.UtcNow });
            await _db.SaveChangesAsync();
        }

        TempData["Success"] = $"{service} installed.";
        return RedirectToTab("services");
    }

    // ---------------- Domains / users ----------------

    public async Task<IActionResult> OnPostMoveDomainAsync(int domainId, int targetNodeId)
    {
        if (!await LoadAsync()) return NotFound();
        return RedirectToPage("/Admin/Nodes/Migrate", new { domainId, fromNodeId = Id, toNodeId = targetNodeId });
    }

    public async Task<IActionResult> OnPostMoveUserAsync(string userId, int targetNodeId)
    {
        if (!await LoadAsync()) return NotFound();
        await _nodes.AssignUserToNodeAsync(userId, targetNodeId);
        TempData["Success"] = "User reassigned to the target node.";
        return RedirectToTab("users");
    }

    // ---------------- Alerts ----------------

    public async Task<IActionResult> OnPostAcknowledgeAsync(int alertId)
    {
        if (!await LoadAsync()) return NotFound();
        var alert = await _db.NodeAlerts.FirstOrDefaultAsync(a => a.Id == alertId && a.NodeId == Id);
        if (alert != null)
        {
            alert.IsAcknowledged = true;
            alert.AcknowledgedAt = DateTime.UtcNow;
            alert.AcknowledgedBy = User.Identity?.Name;
            await _db.SaveChangesAsync();
        }
        TempData["Success"] = "Alert acknowledged.";
        return RedirectToTab("alerts");
    }

    public async Task<IActionResult> OnPostThresholdsAsync(int cpu, int ram, int disk)
    {
        if (!await LoadAsync()) return NotFound();
        Node.CpuThreshold = Math.Clamp(cpu, 50, 100);
        Node.RamThreshold = Math.Clamp(ram, 50, 100);
        Node.DiskThreshold = Math.Clamp(disk, 50, 100);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Alert thresholds updated.";
        return RedirectToTab("alerts");
    }

    // ---------------- Maintenance ----------------

    public async Task<IActionResult> OnPostMaintenanceAsync(bool enable)
    {
        if (!await LoadAsync()) return NotFound();
        Node.Status = enable ? NodeStatus.Maintenance : NodeStatus.Online;
        await _db.SaveChangesAsync();

        if (enable)
            await _ssh.ExecuteCommandAsync(Node, "touch /etc/nginx/maintenance.on && systemctl reload nginx");
        else
            await _ssh.ExecuteCommandAsync(Node, "rm -f /etc/nginx/maintenance.on && systemctl reload nginx");

        await _auditLog.LogAsync(enable ? "MaintenanceOn" : "MaintenanceOff", "ServerNode", Id.ToString(), Node.Name);
        TempData["Success"] = enable ? "Maintenance mode enabled — sites show the maintenance page." : "Maintenance mode disabled.";
        return RedirectToTab("maintenance");
    }

    public async Task<IActionResult> OnPostSystemUpdateAsync()
    {
        if (!await LoadAsync()) return NotFound();
        var result = await _ssh.ExecuteCommandAsync(Node, "apt-get update && apt-get -y upgrade");
        TempData["Success"] = $"System update issued on {Node.Name}{(result.Simulated ? " (simulated)" : "")}.";
        return RedirectToTab("maintenance");
    }

    public async Task<IActionResult> OnPostRebootAsync()
    {
        if (!await LoadAsync()) return NotFound();
        await _ssh.ExecuteCommandAsync(Node, "systemctl reboot");
        await _auditLog.LogAsync("Reboot", "ServerNode", Id.ToString(), Node.Name);
        TempData["Success"] = $"Reboot issued on {Node.Name}.";
        return RedirectToTab("maintenance");
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (!await LoadAsync()) return NotFound();
        if (await _db.DomainNodes.AnyAsync(d => d.NodeId == Id))
        {
            TempData["Error"] = "Move or remove the domains on this node before deleting it.";
            return RedirectToTab("maintenance");
        }

        _db.ServerNodes.Remove(Node);
        await _db.SaveChangesAsync();
        await _auditLog.LogAsync("Delete", "ServerNode", Id.ToString(), Node.Name);
        TempData["Success"] = "Node removed from the fleet.";
        return RedirectToPage("/Admin/Nodes/Index");
    }

    private static string InstallPackage(ServerServiceType service) => service switch
    {
        ServerServiceType.Nginx => "nginx",
        ServerServiceType.MySQL => "mysql-server",
        ServerServiceType.PHP => "php8.3-fpm",
        ServerServiceType.FTP => "vsftpd",
        ServerServiceType.Email => "postfix dovecot-imapd",
        ServerServiceType.DNS => "bind9",
        _ => "rsync"
    };

    private IActionResult RedirectToTab(string tab) => RedirectToPage(new { id = Id, tab });
}
