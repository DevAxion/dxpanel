using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.AppHosting;

namespace SRXPanel.Pages.Client.Apps.Hosted;

public class VirtualenvModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHostedAppService _apps;
    private readonly IRuntimeService _runtimes;

    public VirtualenvModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        IHostedAppService apps, IRuntimeService runtimes)
    {
        _db = db;
        _userManager = userManager;
        _apps = apps;
        _runtimes = runtimes;
    }

    [BindProperty(SupportsGet = true)] public int Id { get; set; }

    public HostedApp App { get; private set; } = null!;
    public List<(string name, string version)> Packages { get; private set; } = new();
    public List<AppRuntime> PythonVersions { get; private set; } = new();
    public int VenvSizeMB { get; private set; }

    private string Uid => _userManager.GetUserId(User)!;

    private async Task<bool> LoadAsync()
    {
        var app = await _db.HostedApps.FirstOrDefaultAsync(a => a.Id == Id && a.UserId == Uid);
        if (app == null || app.Type != AppRuntimeType.Python) return false;
        App = app;
        PythonVersions = await _runtimes.GetInstalledVersionsAsync(AppRuntimeType.Python);
        if (app.VirtualenvCreated)
        {
            Packages = await _apps.PipListAsync(app);
            VenvSizeMB = 40 + Packages.Count * 6; // rough sim size
        }
        return true;
    }

    public async Task<IActionResult> OnGetAsync() => await LoadAsync() ? Page() : NotFound();

    public async Task<IActionResult> OnPostCreateAsync(string pythonVersion)
    {
        if (!await LoadAsync()) return NotFound();
        await _runtimes.CreateVirtualenvAsync(Id, pythonVersion);
        TempData["Success"] = "Virtualenv created.";
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostInstallAsync(string package)
    {
        if (!await LoadAsync()) return NotFound();
        var output = await _apps.PipInstallAsync(App, package);
        TempData["Success"] = output;
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostUninstallAsync(string package)
    {
        if (!await LoadAsync()) return NotFound();
        var output = await _apps.PipUninstallAsync(App, package);
        TempData["Success"] = output;
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostFreezeAsync()
    {
        if (!await LoadAsync()) return NotFound();
        var output = await _apps.PipFreezeAsync(App);
        TempData["Success"] = output;
        return RedirectToPage(new { id = Id });
    }
}
