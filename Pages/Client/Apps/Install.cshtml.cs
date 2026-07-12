using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Apps;

namespace SRXPanel.Pages.Client.Apps;

public class InstallModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IAppInstallerService _installer;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IFileManagerService _files;

    public InstallModel(ApplicationDbContext db, IAppInstallerService installer,
        UserManager<ApplicationUser> userManager, IFileManagerService files)
    {
        _db = db;
        _installer = installer;
        _userManager = userManager;
        _files = files;
    }

    public AppDefinition App { get; private set; } = null!;
    public List<Domain> Domains { get; private set; } = new();
    public long FreeDiskMB { get; private set; }
    public bool EnoughDisk => FreeDiskMB >= App.MinDiskMB;

    [BindProperty] public InputModel Input { get; set; } = new();

    public static readonly string[] PhpVersions = { "8.0", "8.1", "8.2", "8.3" };
    public static readonly (string Code, string Name)[] Languages =
        { ("en", "English"), ("az", "Azərbaycan"), ("tr", "Türkçe"), ("de", "Deutsch"), ("fr", "Français"), ("es", "Español") };

    public class InputModel
    {
        public int AppId { get; set; }
        [Required] public int DomainId { get; set; }
        public string Path { get; set; } = "/";

        [Required, StringLength(200)] public string SiteTitle { get; set; } = "My Site";
        [Required, StringLength(120)] public string AdminUser { get; set; } = "admin";
        [Required, StringLength(100, MinimumLength = 8)] public string AdminPass { get; set; } = "";
        [Required, EmailAddress] public string AdminEmail { get; set; } = "";

        [StringLength(64)] public string DbName { get; set; } = "";
        [StringLength(20)] public string TablePrefix { get; set; } = "wp_";
        public string PhpVersion { get; set; } = "8.3";
        public string Language { get; set; } = "en";
        public bool DebugMode { get; set; }
    }

    private async Task<bool> LoadAsync(int appId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return false;

        var app = await _installer.GetAppByIdAsync(appId);
        if (app == null) return false;
        App = app;

        Domains = await _db.Domains.Where(d => d.UserId == user.Id).OrderBy(d => d.DomainName).ToListAsync();

        var usedMB = _files.GetUsedBytes(user.Id) / 1024 / 1024;
        FreeDiskMB = user.DiskQuotaMB > 0 ? Math.Max(0, user.DiskQuotaMB - usedMB) : long.MaxValue / 2;
        return true;
    }

    public async Task<IActionResult> OnGetAsync(int appId)
    {
        if (!await LoadAsync(appId)) return NotFound();

        var user = await _userManager.GetUserAsync(User);
        Input = new InputModel
        {
            AppId = appId,
            SiteTitle = $"My {App.Name} Site",
            AdminEmail = user?.Email ?? "",
            TablePrefix = App.Slug is "wordpress" or "woocommerce" ? "wp_" : "app_",
            PhpVersion = App.MinPhpVersion == "8.2" ? "8.3" : "8.3",
            DbName = SuggestDbName(user?.UserName ?? "user", App.Slug),
            DomainId = Domains.FirstOrDefault()?.Id ?? 0
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadAsync(Input.AppId)) return NotFound();
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!Domains.Any(d => d.Id == Input.DomainId))
            ModelState.AddModelError("Input.DomainId", "Select one of your domains.");

        if (!ModelState.IsValid) return Page();

        var req = new InstallRequest(
            App.Id, Input.DomainId, Input.Path, Input.SiteTitle,
            Input.AdminUser, Input.AdminPass, Input.AdminEmail,
            App.RequiresDatabase ? Input.DbName : "", Input.TablePrefix, Input.PhpVersion, Input.Language);

        var jobId = await _installer.InstallAsync(user.Id, req);
        return RedirectToPage("/Client/Apps/Progress", new { jobId });
    }

    private static string SuggestDbName(string userName, string slug)
    {
        var prefix = HostingHelpers.UserPrefix(userName);
        var suffix = new string(slug.Where(char.IsLetterOrDigit).Take(6).ToArray());
        var rnd = Random.Shared.Next(100, 999);
        return $"{prefix}_{suffix}{rnd}";
    }
}
