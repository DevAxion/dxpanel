using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.AppHosting;

namespace SRXPanel.Pages.Client.Apps.Hosted;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHostedAppService _apps;
    private readonly IPortManagerService _ports;
    private readonly IAuditLogService _auditLog;

    public IndexModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        IHostedAppService apps, IPortManagerService ports, IAuditLogService auditLog)
    {
        _db = db;
        _userManager = userManager;
        _apps = apps;
        _ports = ports;
        _auditLog = auditLog;
    }

    public List<HostedApp> Apps { get; private set; } = new();
    public int Limit { get; private set; }
    public bool AtLimit { get; private set; }

    private string Uid => _userManager.GetUserId(User)!;

    public async Task OnGetAsync()
    {
        Apps = await _db.HostedApps.Include(a => a.Domain).Include(a => a.Runtime)
            .Where(a => a.UserId == Uid).OrderByDescending(a => a.CreatedAt).ToListAsync();
        Limit = _ports.PerUserLimit;
        AtLimit = await _ports.UserAtLimitAsync(Uid);
    }

    public async Task<IActionResult> OnPostPowerAsync(int id, string action)
    {
        var app = await _db.HostedApps.FirstOrDefaultAsync(a => a.Id == id && a.UserId == Uid);
        if (app == null) return NotFound();

        var ok = action switch
        {
            "start" => await _apps.StartAsync(id),
            "stop" => await _apps.StopAsync(id),
            "restart" => await _apps.RestartAsync(id),
            _ => false
        };
        await _auditLog.LogAsync(action, "HostedApp", id.ToString(), app.Name);
        TempData[ok ? "Success" : "Error"] = ok ? $"{app.Name}: {action} issued." : "Action failed.";
        return RedirectToPage();
    }
}
