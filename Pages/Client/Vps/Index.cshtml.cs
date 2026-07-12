using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Vps;

namespace SRXPanel.Pages.Client.Vps;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IVpsManagerService _vps;
    private readonly IAuditLogService _auditLog;

    public IndexModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        IVpsManagerService vps, IAuditLogService auditLog)
    {
        _db = db;
        _userManager = userManager;
        _vps = vps;
        _auditLog = auditLog;
    }

    public List<VpsInstance> Instances { get; private set; } = new();
    public List<VpsPlan> Plans { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var userId = _userManager.GetUserId(User)!;
        Instances = await _db.VpsInstances.Include(v => v.Node)
            .Where(v => v.UserId == userId && v.Status != VpsStatus.Deleted)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync();
        Plans = (await _db.VpsPlans.Where(p => p.IsActive).ToListAsync())
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Price).ToList();
    }

    public async Task<IActionResult> OnPostPowerAsync(int id, string action)
    {
        var userId = _userManager.GetUserId(User)!;
        var vps = await _db.VpsInstances.Include(v => v.Node)
            .FirstOrDefaultAsync(v => v.Id == id && v.UserId == userId);
        if (vps == null) return NotFound();
        if (vps.Status == VpsStatus.Suspended)
        {
            TempData["Error"] = "This VPS is suspended. Contact support to restore it.";
            return RedirectToPage();
        }

        var type = action switch
        {
            "start" => VpsActionType.Start,
            "stop" => VpsActionType.Stop,
            "restart" => VpsActionType.Restart,
            _ => VpsActionType.Start
        };
        var result = await _vps.PowerActionAsync(vps, type, userId);
        await _auditLog.LogAsync(action, "VpsInstance", id.ToString(), vps.Hostname);
        TempData[result.Status == VpsActionStatus.Success ? "Success" : "Error"] =
            result.Status == VpsActionStatus.Success ? $"{vps.Hostname}: {action} issued." : $"Action failed: {result.Output}";
        return RedirectToPage();
    }
}
