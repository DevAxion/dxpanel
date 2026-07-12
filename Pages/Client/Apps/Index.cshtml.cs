using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services.Apps;

namespace SRXPanel.Pages.Client.Apps;

public class IndexModel : PageModel
{
    private readonly IAppInstallerService _installer;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(IAppInstallerService installer, UserManager<ApplicationUser> userManager)
    {
        _installer = installer;
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public AppCategory? Category { get; set; }

    public List<AppInstallation> Installations { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return;

        var all = await _installer.GetInstallationsAsync(user.Id);
        if (Category.HasValue) all = all.Where(i => i.AppDefinition?.Category == Category).ToList();
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var s = Search.Trim();
            all = all.Where(i => (i.SiteTitle?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false)
                              || (i.AppDefinition?.Name.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false)
                              || (i.Domain?.DomainName.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        }
        Installations = all;
    }

    public async Task<IActionResult> OnPostUpdateAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var jobId = await _installer.UpdateAsync(user.Id, id);
        return RedirectToPage("/Client/Apps/Progress", new { jobId });
    }

    public async Task<IActionResult> OnPostUninstallAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var jobId = await _installer.UninstallAsync(user.Id, id);
        return RedirectToPage("/Client/Apps/Progress", new { jobId });
    }
}
