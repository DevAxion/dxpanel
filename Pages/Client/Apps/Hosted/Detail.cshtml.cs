using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.AppHosting;

namespace SRXPanel.Pages.Client.Apps.Hosted;

public class DetailModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHostedAppService _apps;
    private readonly IAuditLogService _auditLog;

    public DetailModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        IHostedAppService apps, IAuditLogService auditLog)
    {
        _db = db;
        _userManager = userManager;
        _apps = apps;
        _auditLog = auditLog;
    }

    [BindProperty(SupportsGet = true)] public int Id { get; set; }
    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "overview";

    public HostedApp App { get; private set; } = null!;
    public List<HostedAppMetric> History { get; private set; } = new();
    public List<HostedAppEnv> EnvVars { get; private set; } = new();
    public List<HostedAppDeploy> Deploys { get; private set; } = new();
    public List<GitRepository> Repos { get; private set; } = new();
    public List<HostedAppHealthIncident> Incidents { get; private set; } = new();
    public double UptimePercent { get; private set; } = 100;
    public string LogOut { get; private set; } = "";
    public string LogErr { get; private set; } = "";

    private string Uid => _userManager.GetUserId(User)!;

    private async Task<bool> LoadAsync()
    {
        var app = await _db.HostedApps.Include(a => a.Domain).Include(a => a.Runtime).Include(a => a.EnvVars)
            .FirstOrDefaultAsync(a => a.Id == Id && a.UserId == Uid);
        if (app == null) return false;
        App = app;
        return true;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync()) return NotFound();

        switch (Tab)
        {
            case "logs":
                (LogOut, LogErr) = await _apps.GetLogsAsync(App, 200);
                break;
            case "environment":
                EnvVars = App.EnvVars.OrderBy(e => e.Key).ToList();
                break;
            case "deploy":
                Deploys = await _db.HostedAppDeploys.Where(d => d.HostedAppId == Id)
                    .OrderByDescending(d => d.CreatedAt).Take(20).ToListAsync();
                Repos = await _db.GitRepositories.Where(r => r.UserId == Uid).ToListAsync();
                break;
            case "process":
            case "settings":
                break;
            default: // overview
                History = await _db.HostedAppMetrics.Where(m => m.HostedAppId == Id)
                    .OrderBy(m => m.Timestamp).ToListAsync();
                Incidents = await _db.HostedAppHealthIncidents.Where(i => i.HostedAppId == Id)
                    .OrderByDescending(i => i.StartedAt).Take(10).ToListAsync();
                UptimePercent = await ComputeUptimeAsync();
                break;
        }
        return Page();
    }

    private async Task<double> ComputeUptimeAsync()
    {
        var since = DateTime.UtcNow.AddDays(-30);
        var downtime = await _db.HostedAppHealthIncidents
            .Where(i => i.HostedAppId == Id && i.StartedAt >= since)
            .ToListAsync();
        var totalDown = downtime.Sum(i => ((i.EndedAt ?? DateTime.UtcNow) - i.StartedAt).TotalSeconds);
        var window = (DateTime.UtcNow - (App.CreatedAt > since ? App.CreatedAt : since)).TotalSeconds;
        return window <= 0 ? 100 : Math.Round(Math.Max(0, 100 - 100 * totalDown / window), 2);
    }

    public async Task<IActionResult> OnGetMetricsAsync()
    {
        if (!await LoadAsync()) return NotFound();
        var m = await _apps.GetMetricsAsync(App);
        return new JsonResult(new { cpu = m.CpuPercent, mem = m.MemoryMB, uptime = m.UptimeSeconds, status = App.Status.ToString() });
    }

    public async Task<IActionResult> OnGetLogTailAsync()
    {
        if (!await LoadAsync()) return NotFound();
        var (o, e) = await _apps.GetLogsAsync(App, 200);
        return new JsonResult(new { stdout = o, stderr = e });
    }

    private IActionResult ToTab(string tab) => RedirectToPage(new { id = Id, tab });

    public async Task<IActionResult> OnPostPowerAsync(string action)
    {
        if (!await LoadAsync()) return NotFound();
        var ok = action switch
        {
            "start" => await _apps.StartAsync(Id),
            "stop" => await _apps.StopAsync(Id),
            "restart" => await _apps.RestartAsync(Id),
            "reload" => await _apps.ReloadAsync(Id),
            _ => false
        };
        await _auditLog.LogAsync(action, "HostedApp", Id.ToString(), App.Name);
        TempData[ok ? "Success" : "Error"] = ok ? $"{action} issued." : "Action failed.";
        return ToTab(Tab == "overview" ? "overview" : Tab);
    }

    public async Task<IActionResult> OnPostScaleAsync(int instances, bool clusterMode, int maxMemory)
    {
        if (!await LoadAsync()) return NotFound();
        await _apps.ScaleAsync(Id, instances);
        App.MaxMemoryRestartMB = Math.Clamp(maxMemory, 64, 4096);
        App.ClusterMode = clusterMode;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Process settings updated.";
        return ToTab("process");
    }

    public async Task<IActionResult> OnPostEnvSetAsync(string key, string value, bool isSecret)
    {
        if (!await LoadAsync()) return NotFound();
        await _apps.SetEnvAsync(Id, key, value, isSecret);
        TempData["Success"] = "Variable saved. Restart the app to apply.";
        return ToTab("environment");
    }

    public async Task<IActionResult> OnPostEnvDeleteAsync(int envId)
    {
        if (!await LoadAsync()) return NotFound();
        await _apps.DeleteEnvAsync(Id, envId);
        TempData["Success"] = "Variable removed.";
        return ToTab("environment");
    }

    public async Task<IActionResult> OnPostEnvImportAsync(string dotenv)
    {
        if (!await LoadAsync()) return NotFound();
        var n = await _apps.ImportEnvAsync(Id, dotenv ?? "");
        TempData["Success"] = $"Imported {n} variable(s).";
        return ToTab("environment");
    }

    public async Task<IActionResult> OnPostDeployAsync(string type, int? gitRepoId)
    {
        if (!await LoadAsync()) return NotFound();
        var deployType = type == "git" ? AppDeployType.Git : type == "upload" ? AppDeployType.Upload : AppDeployType.Manual;
        _apps.StartDeploy(Id, deployType, gitRepoId);
        await _auditLog.LogAsync("Deploy", "HostedApp", Id.ToString(), App.Name);
        TempData["Success"] = "Deploy started — watch the progress below.";
        return ToTab("deploy");
    }

    public async Task<IActionResult> OnPostSettingsAsync(string entryPoint, int port, int processCount,
        int maxMemory, bool watchMode)
    {
        if (!await LoadAsync()) return NotFound();
        App.EntryPoint = entryPoint?.Trim() ?? App.EntryPoint;
        if (port is >= 3000 and <= 9999 && !await _db.HostedApps.AnyAsync(a => a.Port == port && a.Id != Id))
            App.Port = port;
        App.ProcessCount = Math.Clamp(processCount, 1, 4);
        App.MaxMemoryRestartMB = Math.Clamp(maxMemory, 64, 4096);
        App.WatchMode = watchMode;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Settings saved. Restart to apply.";
        return ToTab("settings");
    }

    public async Task<IActionResult> OnPostFlushLogsAsync()
    {
        if (!await LoadAsync()) return NotFound();
        await _apps.FlushLogsAsync(App);
        TempData["Success"] = "Logs cleared.";
        return ToTab("logs");
    }

    public async Task<IActionResult> OnPostDeleteAsync(string confirm)
    {
        if (!await LoadAsync()) return NotFound();
        if (!string.Equals(confirm, App.Name, StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Type the app name to confirm.";
            return ToTab("settings");
        }
        await _apps.DeleteAsync(Id);
        await _auditLog.LogAsync("Delete", "HostedApp", Id.ToString(), App.Name);
        TempData["Success"] = "App deleted.";
        return RedirectToPage("/Client/Apps/Hosted/Index");
    }
}
