using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Vps;

namespace SRXPanel.Pages.Client.Vps;

public class DetailModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IVpsManagerService _vps;
    private readonly IProxmoxService _proxmox;
    private readonly IAuditLogService _auditLog;

    public DetailModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IVpsManagerService vps,
        IProxmoxService proxmox, IAuditLogService auditLog)
    {
        _db = db;
        _userManager = userManager;
        _vps = vps;
        _proxmox = proxmox;
        _auditLog = auditLog;
    }

    [BindProperty(SupportsGet = true)] public int Id { get; set; }
    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "overview";

    public VpsInstance Vps { get; private set; } = null!;
    public List<VpsMetric> History { get; private set; } = new();
    public List<VpsSnapshot> Snapshots { get; private set; } = new();
    public List<VpsBackup> Backups { get; private set; } = new();
    public List<VpsTemplate> Templates { get; private set; } = new();
    public List<VpsPlan> UpgradePlans { get; private set; } = new();
    public List<VpsFirewallRule> FirewallRules { get; private set; } = new();
    public VpsPlan? CurrentPlan { get; private set; }

    protected virtual IQueryable<VpsInstance> Scope(string userId) =>
        _db.VpsInstances.Where(v => v.UserId == userId);

    private async Task<bool> LoadAsync()
    {
        var userId = _userManager.GetUserId(User)!;
        var vps = await Scope(userId).Include(v => v.Node).Include(v => v.FirewallRules)
            .FirstOrDefaultAsync(v => v.Id == Id && v.Status != VpsStatus.Deleted);
        if (vps == null) return false;
        Vps = vps;
        CurrentPlan = await _db.VpsPlans.FirstOrDefaultAsync(p => p.Id == vps.PlanId);
        return true;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync()) return NotFound();

        switch (Tab)
        {
            case "snapshots":
                Snapshots = await _db.VpsSnapshots.Where(s => s.VpsInstanceId == Id && s.Status != VpsSnapshotStatus.Deleted)
                    .OrderByDescending(s => s.CreatedAt).ToListAsync();
                break;
            case "backups":
                Backups = await _db.VpsBackups.Where(b => b.VpsInstanceId == Id && b.Status != VpsBackupStatus.Deleted)
                    .OrderByDescending(b => b.CreatedAt).ToListAsync();
                break;
            case "rebuild":
                Templates = await _db.VpsTemplates.Where(t => t.IsActive && t.NodeId == Vps.NodeId).ToListAsync();
                break;
            case "resize":
                UpgradePlans = (await _db.VpsPlans.Where(p => p.IsActive && p.Id != Vps.PlanId
                        && (p.CpuCores >= Vps.CpuCores && p.RamMB >= Vps.RamMB && p.DiskGB >= Vps.DiskGB)).ToListAsync())
                    .OrderBy(p => p.Price).ToList();
                break;
            case "network":
                FirewallRules = Vps.FirewallRules.OrderBy(r => r.Port).ToList();
                break;
            default: // overview
                History = await _db.VpsMetrics.Where(m => m.VpsInstanceId == Id)
                    .OrderBy(m => m.Timestamp).ToListAsync();
                break;
        }
        return Page();
    }

    /// <summary>SignalR fallback: live stats as JSON.</summary>
    public async Task<IActionResult> OnGetStatsAsync()
    {
        if (!await LoadAsync()) return NotFound();
        if (Vps.Node == null || Vps.Status != VpsStatus.Running)
            return new JsonResult(new { cpu = 0, ram = 0, disk = 0, netIn = 0, netOut = 0, bwUsed = Vps.BandwidthUsed, bwPct = Vps.BandwidthPercent });
        var s = await _proxmox.GetVmStatsAsync(Vps.Node, Vps.VmId);
        return new JsonResult(new { cpu = s.CpuPercent, ram = s.RamPercent, disk = s.DiskPercent, netIn = s.NetworkInMbps, netOut = s.NetworkOutMbps, bwUsed = Vps.BandwidthUsed, bwPct = Vps.BandwidthPercent });
    }

    private IActionResult ToTab(string tab) => RedirectToPage(new { id = Id, tab });

    // ---------------- Power ----------------

    public async Task<IActionResult> OnPostPowerAsync(string action)
    {
        if (!await LoadAsync()) return NotFound();
        if (Vps.Status == VpsStatus.Suspended) { TempData["Error"] = "VPS is suspended."; return ToTab("power"); }

        var type = action switch
        {
            "start" => VpsActionType.Start,
            "stop" => VpsActionType.Stop,
            "restart" => VpsActionType.Restart,
            "shutdown" => VpsActionType.Shutdown,
            _ => VpsActionType.Start
        };
        var result = await _vps.PowerActionAsync(Vps, type, _userManager.GetUserId(User)!);
        await _auditLog.LogAsync(action, "VpsInstance", Id.ToString(), Vps.Hostname);
        TempData[result.Status == VpsActionStatus.Success ? "Success" : "Error"] =
            result.Status == VpsActionStatus.Success ? $"{action} issued." : $"Failed: {result.Output}";
        return ToTab("power");
    }

    // ---------------- Rebuild ----------------

    public async Task<IActionResult> OnPostRebuildAsync(int templateId, string newPassword, string confirm)
    {
        if (!await LoadAsync()) return NotFound();
        if (!string.Equals(confirm, "REBUILD", StringComparison.Ordinal))
        {
            TempData["Error"] = "Type REBUILD to confirm.";
            return ToTab("rebuild");
        }
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            TempData["Error"] = "New root password must be at least 8 characters.";
            return ToTab("rebuild");
        }
        var action = await _vps.RebuildAsync(Vps, templateId, newPassword, _userManager.GetUserId(User)!);
        await _auditLog.LogAsync("Rebuild", "VpsInstance", Id.ToString(), Vps.Hostname);
        TempData[action.Status == VpsActionStatus.Success ? "Success" : "Error"] =
            action.Status == VpsActionStatus.Success ? "VPS rebuild started with a fresh OS image." : $"Rebuild failed: {action.Output}";
        return ToTab("rebuild");
    }

    // ---------------- Resize ----------------

    public async Task<IActionResult> OnPostResizeAsync(int targetPlanId)
    {
        if (!await LoadAsync()) return NotFound();
        var plan = await _db.VpsPlans.FirstOrDefaultAsync(p => p.Id == targetPlanId && p.IsActive);
        if (plan == null) { TempData["Error"] = "Plan not found."; return ToTab("resize"); }
        if (plan.CpuCores < Vps.CpuCores || plan.RamMB < Vps.RamMB || plan.DiskGB < Vps.DiskGB)
        {
            TempData["Error"] = "Downgrades that shrink the disk are not supported online.";
            return ToTab("resize");
        }

        var action = await _vps.ResizeAsync(Vps, plan.CpuCores, plan.RamMB, plan.DiskGB, _userManager.GetUserId(User)!);
        if (action.Status == VpsActionStatus.Success)
        {
            Vps.PlanId = plan.Id;
            Vps.BandwidthGB = plan.BandwidthGB;
            await _db.SaveChangesAsync();
        }
        await _auditLog.LogAsync("Resize", "VpsInstance", Id.ToString(), $"{Vps.Hostname} → {plan.Name}");
        TempData[action.Status == VpsActionStatus.Success ? "Success" : "Error"] =
            action.Status == VpsActionStatus.Success ? $"Resized to {plan.Name}. A restart may be required." : $"Resize failed: {action.Output}";
        return ToTab("resize");
    }

    // ---------------- Snapshots ----------------

    public async Task<IActionResult> OnPostSnapshotCreateAsync(string name)
    {
        if (!await LoadAsync()) return NotFound();
        var max = 5;
        var count = await _db.VpsSnapshots.CountAsync(s => s.VpsInstanceId == Id && s.Status != VpsSnapshotStatus.Deleted);
        if (count >= max) { TempData["Error"] = $"Snapshot limit reached ({max})."; return ToTab("snapshots"); }
        await _vps.CreateSnapshotAsync(Vps, name, _userManager.GetUserId(User)!);
        TempData["Success"] = "Snapshot created.";
        return ToTab("snapshots");
    }

    public async Task<IActionResult> OnPostSnapshotRestoreAsync(int snapshotId)
    {
        if (!await LoadAsync()) return NotFound();
        await _vps.RestoreSnapshotAsync(Vps, snapshotId, _userManager.GetUserId(User)!);
        TempData["Success"] = "Rollback to snapshot started.";
        return ToTab("snapshots");
    }

    public async Task<IActionResult> OnPostSnapshotDeleteAsync(int snapshotId)
    {
        if (!await LoadAsync()) return NotFound();
        await _vps.DeleteSnapshotAsync(Vps, snapshotId, _userManager.GetUserId(User)!);
        TempData["Success"] = "Snapshot deleted.";
        return ToTab("snapshots");
    }

    // ---------------- Backups ----------------

    public async Task<IActionResult> OnPostBackupCreateAsync()
    {
        if (!await LoadAsync()) return NotFound();
        await _vps.CreateBackupAsync(Vps, _userManager.GetUserId(User)!);
        TempData["Success"] = "Backup started.";
        return ToTab("backups");
    }

    public async Task<IActionResult> OnPostBackupRestoreAsync(int backupId)
    {
        if (!await LoadAsync()) return NotFound();
        await _vps.RestoreBackupAsync(Vps, backupId, _userManager.GetUserId(User)!);
        TempData["Success"] = "Restore from backup started.";
        return ToTab("backups");
    }

    public async Task<IActionResult> OnPostBackupDeleteAsync(int backupId)
    {
        if (!await LoadAsync()) return NotFound();
        await _vps.DeleteBackupAsync(Vps, backupId, _userManager.GetUserId(User)!);
        TempData["Success"] = "Backup deleted.";
        return ToTab("backups");
    }

    // ---------------- Network ----------------

    public async Task<IActionResult> OnPostRdnsAsync(string reverseDns)
    {
        if (!await LoadAsync()) return NotFound();
        Vps.ReverseDns = (reverseDns ?? "").Trim();
        await _db.SaveChangesAsync();
        await _proxmox.SetCloudInitAsync(Vps.Node!, Vps.VmId,
            new CloudInitConfig(Vps.Hostname, "root", null, null, Vps.IpAddress ?? "", "", Vps.Ipv6Address));
        TempData["Success"] = "Reverse DNS updated.";
        return ToTab("network");
    }

    public async Task<IActionResult> OnPostFirewallAddAsync(string action, string protocol, int port, string? source)
    {
        if (!await LoadAsync()) return NotFound();
        if (port is < 1 or > 65535) { TempData["Error"] = "Enter a valid port (1-65535)."; return ToTab("network"); }
        _db.VpsFirewallRules.Add(new VpsFirewallRule
        {
            VpsInstanceId = Id,
            Action = action == "deny" ? VpsFirewallAction.Deny : VpsFirewallAction.Allow,
            Protocol = protocol == "udp" ? "udp" : "tcp",
            Port = port,
            Source = string.IsNullOrWhiteSpace(source) ? "any" : source.Trim()
        });
        await _db.SaveChangesAsync();
        await _proxmox.SetCloudInitAsync(Vps.Node!, Vps.VmId,
            new CloudInitConfig(Vps.Hostname, "root", null, null, Vps.IpAddress ?? "", "", Vps.Ipv6Address));
        TempData["Success"] = "Firewall rule added.";
        return ToTab("network");
    }

    public async Task<IActionResult> OnPostFirewallDeleteAsync(int ruleId)
    {
        if (!await LoadAsync()) return NotFound();
        var rule = await _db.VpsFirewallRules.FirstOrDefaultAsync(r => r.Id == ruleId && r.VpsInstanceId == Id);
        if (rule != null) { _db.VpsFirewallRules.Remove(rule); await _db.SaveChangesAsync(); }
        TempData["Success"] = "Firewall rule removed.";
        return ToTab("network");
    }

    // ---------------- Settings ----------------

    public async Task<IActionResult> OnPostSettingsAsync(string hostname, string? notes, bool notifyBandwidth, bool notifyPower)
    {
        if (!await LoadAsync()) return NotFound();
        if (!string.IsNullOrWhiteSpace(hostname)) Vps.Hostname = hostname.Trim();
        Vps.Notes = notes;
        Vps.NotifyBandwidth = notifyBandwidth;
        Vps.NotifyPower = notifyPower;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Settings saved.";
        return ToTab("settings");
    }

    public async Task<IActionResult> OnPostCancelAsync(string confirm)
    {
        if (!await LoadAsync()) return NotFound();
        if (!string.Equals(confirm, Vps.Hostname, StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Type the hostname to confirm cancellation.";
            return ToTab("settings");
        }
        await _vps.DeleteAsync(Vps, _userManager.GetUserId(User)!);
        await _auditLog.LogAsync("Cancel", "VpsInstance", Id.ToString(), Vps.Hostname);
        TempData["Success"] = "Your VPS has been cancelled and destroyed.";
        return RedirectToPage("/Client/Vps/Index");
    }

    // ---------------- Console ----------------

    public async Task<IActionResult> OnPostConsoleAsync()
    {
        if (!await LoadAsync()) return NotFound();
        var console = await _vps.OpenConsoleAsync(Vps, _userManager.GetUserId(User)!);
        return new JsonResult(new { url = console?.Url });
    }
}
