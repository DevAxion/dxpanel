using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.AppHosting;

namespace SRXPanel.Pages.Client.Apps.Hosted;

public class PackagesModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHostedAppService _apps;

    public PackagesModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IHostedAppService apps)
    {
        _db = db;
        _userManager = userManager;
        _apps = apps;
    }

    [BindProperty(SupportsGet = true)] public int Id { get; set; }

    public HostedApp App { get; private set; } = null!;
    public string? Output { get; private set; }
    public List<string> Scripts { get; private set; } = new() { "start", "build", "test", "lint" };

    private string Uid => _userManager.GetUserId(User)!;

    private async Task<bool> LoadAsync()
    {
        var app = await _db.HostedApps.FirstOrDefaultAsync(a => a.Id == Id && a.UserId == Uid);
        if (app == null || app.Type != AppRuntimeType.NodeJs) return false;
        App = app;
        return true;
    }

    public async Task<IActionResult> OnGetAsync() => await LoadAsync() ? Page() : NotFound();

    public async Task<IActionResult> OnPostRunScriptAsync(string script)
    {
        if (!await LoadAsync()) return NotFound();
        Output = await _apps.RunNpmScriptAsync(App, script);
        return Page();
    }

    public async Task<IActionResult> OnPostInstallAsync(string? package)
    {
        if (!await LoadAsync()) return NotFound();
        Output = await _apps.NpmInstallAsync(App, package);
        return Page();
    }

    public async Task<IActionResult> OnPostAuditAsync(bool fix)
    {
        if (!await LoadAsync()) return NotFound();
        Output = await _apps.NpmAuditAsync(App, fix);
        return Page();
    }
}
