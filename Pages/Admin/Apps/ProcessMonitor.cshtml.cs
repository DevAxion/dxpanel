using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.AppHosting;

namespace SRXPanel.Pages.Admin.Apps;

public class ProcessMonitorModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IPm2Service _pm2;
    private readonly IHostedAppService _apps;
    private readonly IAuditLogService _auditLog;

    public ProcessMonitorModel(ApplicationDbContext db, IPm2Service pm2, IHostedAppService apps, IAuditLogService auditLog)
    {
        _db = db;
        _pm2 = pm2;
        _apps = apps;
        _auditLog = auditLog;
    }

    public List<HostedApp> Processes { get; private set; } = new();
    public double TotalCpu { get; private set; }
    public double TotalMemory { get; private set; }
    public bool Simulated { get; private set; }

    public async Task OnGetAsync()
    {
        // "PM2 processes" across the platform are the running Node apps; other runtimes shown too.
        Processes = await _db.HostedApps.Include(a => a.User)
            .Where(a => a.Status == HostedAppStatus.Running || a.Status == HostedAppStatus.Error)
            .OrderByDescending(a => a.MemoryMB).ToListAsync();
        TotalCpu = Processes.Sum(p => p.CpuPercent);
        TotalMemory = Processes.Sum(p => p.MemoryMB);
    }

    public async Task<IActionResult> OnPostKillAsync(int id)
    {
        var app = await _db.HostedApps.FirstOrDefaultAsync(a => a.Id == id);
        if (app == null) return NotFound();
        await _apps.StopAsync(id);
        await _auditLog.LogAsync("Kill", "HostedApp", id.ToString(), app.Name);
        TempData["Success"] = $"Process for {app.Name} stopped.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var result = await _pm2.SavePm2ListAsync();
        TempData["Success"] = $"PM2 process list saved{(result.Success ? "" : " (with warnings)")}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResurrectAsync()
    {
        // Restart all stopped apps that should be running (pm2 resurrect equivalent).
        var stopped = await _db.HostedApps.Where(a => a.Status == HostedAppStatus.Stopped && a.AutoRestart).ToListAsync();
        foreach (var app in stopped) await _apps.StartAsync(app.Id);
        TempData["Success"] = $"Resurrected {stopped.Count} app(s).";
        return RedirectToPage();
    }
}
