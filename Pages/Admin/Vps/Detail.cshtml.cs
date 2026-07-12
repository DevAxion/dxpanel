using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Vps;

namespace SRXPanel.Pages.Admin.Vps;

public class DetailModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IVpsManagerService _vps;
    private readonly IProxmoxService _proxmox;
    private readonly IAuditLogService _auditLog;

    public DetailModel(ApplicationDbContext db, IVpsManagerService vps, IProxmoxService proxmox, IAuditLogService auditLog)
    {
        _db = db;
        _vps = vps;
        _proxmox = proxmox;
        _auditLog = auditLog;
    }

    [BindProperty(SupportsGet = true)] public int Id { get; set; }

    public VpsInstance Vps { get; private set; } = null!;
    public ApplicationUser? Owner { get; private set; }
    public VpsPlan? Plan { get; private set; }
    public List<VpsAction> Actions { get; private set; } = new();
    public List<ProxmoxNode> Nodes { get; private set; } = new();
    public ProxmoxVmStats? LiveStats { get; private set; }

    private async Task<bool> LoadAsync()
    {
        var vps = await _db.VpsInstances.Include(v => v.Node).Include(v => v.User)
            .FirstOrDefaultAsync(v => v.Id == Id);
        if (vps == null) return false;
        Vps = vps;
        Owner = vps.User;
        Plan = await _db.VpsPlans.FirstOrDefaultAsync(p => p.Id == vps.PlanId);
        Actions = await _db.VpsActions.Where(a => a.VpsInstanceId == Id)
            .OrderByDescending(a => a.StartedAt).Take(20).ToListAsync();
        Nodes = await _db.ProxmoxNodes.Where(n => n.IsActive).ToListAsync();
        return true;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync()) return NotFound();
        if (Vps.Node != null && Vps.Status == VpsStatus.Running)
            LiveStats = await _proxmox.GetVmStatsAsync(Vps.Node, Vps.VmId);
        return Page();
    }

    private IActionResult Back() => RedirectToPage(new { id = Id });

    public async Task<IActionResult> OnPostPowerAsync(string action)
    {
        if (!await LoadAsync()) return NotFound();
        var type = action switch
        {
            "start" => VpsActionType.Start, "stop" => VpsActionType.Stop,
            "restart" => VpsActionType.Restart, "shutdown" => VpsActionType.Shutdown, _ => VpsActionType.Start
        };
        await _vps.PowerActionAsync(Vps, type, "admin");
        await _auditLog.LogAsync(action, "VpsInstance", Id.ToString(), Vps.Hostname);
        TempData["Success"] = $"{action} issued.";
        return Back();
    }

    public async Task<IActionResult> OnPostSuspendAsync()
    {
        if (!await LoadAsync()) return NotFound();
        await _vps.SuspendAsync(Vps, "admin");
        await _auditLog.LogAsync("Suspend", "VpsInstance", Id.ToString(), Vps.Hostname);
        TempData["Success"] = "VPS suspended.";
        return Back();
    }

    public async Task<IActionResult> OnPostResumeAsync()
    {
        if (!await LoadAsync()) return NotFound();
        await _vps.ResumeAsync(Vps, "admin");
        await _auditLog.LogAsync("Resume", "VpsInstance", Id.ToString(), Vps.Hostname);
        TempData["Success"] = "VPS resumed.";
        return Back();
    }

    public async Task<IActionResult> OnPostForceDeleteAsync()
    {
        if (!await LoadAsync()) return NotFound();
        await _vps.DeleteAsync(Vps, "admin");
        await _auditLog.LogAsync("ForceDelete", "VpsInstance", Id.ToString(), Vps.Hostname);
        TempData["Success"] = "VPS force-deleted.";
        return RedirectToPage("/Admin/Vps/Index");
    }

    public async Task<IActionResult> OnPostMigrateAsync(int targetNodeId)
    {
        if (!await LoadAsync()) return NotFound();
        var target = await _db.ProxmoxNodes.FirstOrDefaultAsync(n => n.Id == targetNodeId && n.IsActive);
        if (target == null) { TempData["Error"] = "Target node not found."; return Back(); }

        // Simulated live migration: log the Proxmox call and repoint the placement.
        await _proxmox.CreateBackupAsync(Vps.Node!, Vps.VmId, Vps.Node!.Storage);
        Vps.NodeId = target.Id;
        await _db.SaveChangesAsync();
        await _auditLog.LogAsync("Migrate", "VpsInstance", Id.ToString(), $"{Vps.Hostname} → {target.Name}");
        TempData["Success"] = $"VPS migrated to {target.Name}.";
        return Back();
    }

    public async Task<IActionResult> OnPostFreeDaysAsync(int days)
    {
        if (!await LoadAsync()) return NotFound();
        Vps.ExpiresAt = (Vps.ExpiresAt ?? DateTime.UtcNow).AddDays(Math.Clamp(days, 1, 365));
        await _db.SaveChangesAsync();
        await _auditLog.LogAsync("AddFreeDays", "VpsInstance", Id.ToString(), $"+{days}d → {Vps.ExpiresAt:yyyy-MM-dd}");
        TempData["Success"] = $"Added {days} day(s). New expiry {Vps.ExpiresAt:yyyy-MM-dd}.";
        return Back();
    }

    public async Task<IActionResult> OnPostOverrideAsync(int cpuCores, int ramMB, int diskGB, int bandwidthGB)
    {
        if (!await LoadAsync()) return NotFound();
        Vps.CpuCores = Math.Clamp(cpuCores, 1, 128);
        Vps.RamMB = Math.Clamp(ramMB, 256, 524288);
        Vps.DiskGB = Math.Clamp(diskGB, 5, 20000);
        Vps.BandwidthGB = Math.Max(0, bandwidthGB);
        await _vps.ResizeAsync(Vps, Vps.CpuCores, Vps.RamMB, Vps.DiskGB, "admin");
        await _auditLog.LogAsync("OverrideLimits", "VpsInstance", Id.ToString(), Vps.Hostname);
        TempData["Success"] = "Resource limits overridden.";
        return Back();
    }

    public async Task<IActionResult> OnPostNotesAsync(string? internalNotes)
    {
        if (!await LoadAsync()) return NotFound();
        Vps.Notes = internalNotes;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Internal notes saved.";
        return Back();
    }
}
