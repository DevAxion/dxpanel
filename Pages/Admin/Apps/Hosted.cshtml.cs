using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.AppHosting;

namespace SRXPanel.Pages.Admin.Apps;

public class HostedModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IHostedAppService _apps;
    private readonly IPortManagerService _ports;
    private readonly IAuditLogService _auditLog;

    public HostedModel(ApplicationDbContext db, IHostedAppService apps, IPortManagerService ports, IAuditLogService auditLog)
    {
        _db = db;
        _apps = apps;
        _ports = ports;
        _auditLog = auditLog;
    }

    [BindProperty(SupportsGet = true)] public AppRuntimeType? Type { get; set; }
    [BindProperty(SupportsGet = true)] public HostedAppStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public List<HostedApp> Apps { get; private set; } = new();
    public List<HostedApp> TopMemory { get; private set; } = new();
    public int Running { get; private set; }
    public int Total { get; private set; }
    public int PortRangeStart { get; private set; }
    public int PortRangeEnd { get; private set; }

    public async Task OnGetAsync()
    {
        var query = _db.HostedApps.Include(a => a.User).Include(a => a.Domain).AsQueryable();
        if (Type.HasValue) query = query.Where(a => a.Type == Type.Value);
        if (Status.HasValue) query = query.Where(a => a.Status == Status.Value);
        if (!string.IsNullOrWhiteSpace(Q)) query = query.Where(a => a.Name.Contains(Q));

        Apps = await query.OrderByDescending(a => a.CpuPercent).ToListAsync();
        TopMemory = await _db.HostedApps.Where(a => a.Status == HostedAppStatus.Running)
            .OrderByDescending(a => a.MemoryMB).Take(10).ToListAsync();
        Running = await _db.HostedApps.CountAsync(a => a.Status == HostedAppStatus.Running);
        Total = await _db.HostedApps.CountAsync();
        PortRangeStart = _ports.RangeStart;
        PortRangeEnd = _ports.RangeEnd;
    }

    public async Task<IActionResult> OnPostBulkAsync(string action, List<int> ids)
    {
        foreach (var id in ids)
        {
            _ = action switch
            {
                "start" => await _apps.StartAsync(id),
                "stop" => await _apps.StopAsync(id),
                "restart" => await _apps.RestartAsync(id),
                _ => false
            };
        }
        await _auditLog.LogAsync($"Bulk{action}", "HostedApp", string.Join(",", ids), $"{ids.Count} app(s)");
        TempData["Success"] = $"{action} applied to {ids.Count} app(s).";
        return RedirectToPage(new { Type, Status, Q });
    }
}
