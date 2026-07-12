using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.AppHosting;

namespace SRXPanel.Pages.Client.Apps.Hosted;

public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHostedAppService _apps;
    private readonly IPortManagerService _ports;
    private readonly IRuntimeService _runtimes;
    private readonly IAuditLogService _auditLog;

    public CreateModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IHostedAppService apps,
        IPortManagerService ports, IRuntimeService runtimes, IAuditLogService auditLog)
    {
        _db = db;
        _userManager = userManager;
        _apps = apps;
        _ports = ports;
        _runtimes = runtimes;
        _auditLog = auditLog;
    }

    public List<Domain> Domains { get; private set; } = new();
    public List<AppRuntime> Runtimes { get; private set; } = new();
    public List<GitRepository> Repos { get; private set; } = new();
    public IReadOnlyList<AppTemplate> Templates => QuickstartTemplates.All;

    private string Uid => _userManager.GetUserId(User)!;

    private async Task LoadAsync()
    {
        Domains = await _db.Domains.Where(d => d.UserId == Uid).ToListAsync();
        Runtimes = await _runtimes.GetAvailableRuntimesAsync();
        Repos = await _db.GitRepositories.Where(r => r.UserId == Uid).ToListAsync();
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (await _ports.UserAtLimitAsync(Uid))
        {
            TempData["Error"] = $"You have reached your limit of {_ports.PerUserLimit} hosted apps.";
            return RedirectToPage("/Client/Apps/Hosted/Index");
        }
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string name, AppRuntimeType type, int? domainId, int? runtimeId,
        string appPath, string entryPoint, string startCommand, int processCount, bool autoRestart,
        string? envKeys, string? envValues, string deploySource, int? gitRepoId)
    {
        await LoadAsync();

        if (string.IsNullOrWhiteSpace(name)) { TempData["Error"] = "Enter an app name."; return Page(); }
        if (string.IsNullOrWhiteSpace(appPath)) { TempData["Error"] = "Enter the app path."; return Page(); }
        if (await _ports.UserAtLimitAsync(Uid)) { TempData["Error"] = "App limit reached."; return Page(); }

        // Parse the parallel env key/value arrays from the editor.
        var env = new Dictionary<string, string>();
        var keys = (envKeys ?? "").Split('\n');
        var values = (envValues ?? "").Split('\n');
        for (var i = 0; i < keys.Length; i++)
        {
            var k = keys[i].Trim();
            if (k.Length == 0) continue;
            env[k] = i < values.Length ? values[i].Trim() : "";
        }

        var app = await _apps.CreateAppAsync(new CreateAppRequest(
            Uid, name.Trim(), type, domainId, runtimeId,
            appPath.Trim(), entryPoint?.Trim() ?? "", startCommand?.Trim() ?? "",
            Math.Clamp(processCount, 1, 4), autoRestart, env));

        await _auditLog.LogAsync("Create", "HostedApp", app.Id.ToString(), app.Name);

        // Kick off an initial deploy (installs deps + starts) when a source is chosen.
        if (deploySource is "git" or "upload" or "existing")
        {
            var deployType = deploySource switch
            {
                "git" => AppDeployType.Git,
                "upload" => AppDeployType.Upload,
                _ => AppDeployType.Manual
            };
            _apps.StartDeploy(app.Id, deployType, deploySource == "git" ? gitRepoId : null);
            await _apps.StartAsync(app.Id);
        }

        TempData["Success"] = "App created. Watch the deploy progress on the overview.";
        return RedirectToPage("/Client/Apps/Hosted/Detail", new { id = app.Id });
    }
}
