using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Vps;

namespace SRXPanel.Pages.Admin.Vps;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IVpsManagerService _vps;
    private readonly IAuditLogService _auditLog;

    public IndexModel(ApplicationDbContext db, IVpsManagerService vps, IAuditLogService auditLog)
    {
        _db = db;
        _vps = vps;
        _auditLog = auditLog;
    }

    [BindProperty(SupportsGet = true)] public VpsStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public int? NodeFilter { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public List<VpsInstance> Instances { get; private set; } = new();
    public List<ProxmoxNode> Nodes { get; private set; } = new();

    public int TotalRunning { get; private set; }
    public int TotalSuspended { get; private set; }
    public int TotalBuilding { get; private set; }

    public async Task OnGetAsync()
    {
        Nodes = await _db.ProxmoxNodes.ToListAsync();

        var query = _db.VpsInstances.Include(v => v.Node).Include(v => v.User).AsQueryable();
        if (Status.HasValue) query = query.Where(v => v.Status == Status.Value);
        else query = query.Where(v => v.Status != VpsStatus.Deleted);
        if (NodeFilter.HasValue) query = query.Where(v => v.NodeId == NodeFilter.Value);
        if (!string.IsNullOrWhiteSpace(Q))
            query = query.Where(v => v.Hostname.Contains(Q) || (v.IpAddress != null && v.IpAddress.Contains(Q)));

        Instances = await query.OrderByDescending(v => v.CreatedAt).ToListAsync();

        TotalRunning = await _db.VpsInstances.CountAsync(v => v.Status == VpsStatus.Running);
        TotalSuspended = await _db.VpsInstances.CountAsync(v => v.Status == VpsStatus.Suspended);
        TotalBuilding = await _db.VpsInstances.CountAsync(v => v.Status == VpsStatus.Building);
    }

    public async Task<IActionResult> OnPostBulkAsync(string action, List<int> ids)
    {
        var instances = await _db.VpsInstances.Include(v => v.Node)
            .Where(v => ids.Contains(v.Id) && v.Status != VpsStatus.Deleted).ToListAsync();

        foreach (var vps in instances)
        {
            switch (action)
            {
                case "suspend" when vps.Status != VpsStatus.Suspended: await _vps.SuspendAsync(vps, "admin"); break;
                case "resume" when vps.Status == VpsStatus.Suspended: await _vps.ResumeAsync(vps, "admin"); break;
                case "delete": await _vps.DeleteAsync(vps, "admin"); break;
            }
        }
        await _auditLog.LogAsync($"Bulk{action}", "VpsInstance", string.Join(",", ids), $"{instances.Count} instance(s)");
        TempData["Success"] = $"{action} applied to {instances.Count} instance(s).";
        return RedirectToPage(new { Status, NodeFilter, Q });
    }
}
