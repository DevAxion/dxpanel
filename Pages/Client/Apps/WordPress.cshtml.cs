using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services.Apps;

namespace SRXPanel.Pages.Client.Apps;

public class WordPressModel : PageModel
{
    private readonly IAppInstallerService _installer;
    private readonly IWordPressManager _wp;
    private readonly UserManager<ApplicationUser> _userManager;

    public WordPressModel(IAppInstallerService installer, IWordPressManager wp, UserManager<ApplicationUser> userManager)
    {
        _installer = installer;
        _wp = wp;
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "dashboard";
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public AppInstallation Installation { get; private set; } = null!;
    public WpHealth Health { get; private set; } = null!;
    public List<WpAsset> Plugins { get; private set; } = new();
    public List<WpAsset> Themes { get; private set; } = new();
    public List<RepoAsset> RepoResults { get; private set; } = new();

    private async Task<bool> LoadAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return false;

        var inst = await _installer.GetInstallationDetailsAsync(user.Id, id);
        if (inst == null || inst.AppDefinition?.Slug is not ("wordpress" or "woocommerce")) return false;
        Installation = inst;

        Health = await _wp.GetHealthAsync(id);
        Plugins = await _wp.GetAssetsAsync(id, WpAssetType.Plugin);
        Themes = await _wp.GetAssetsAsync(id, WpAssetType.Theme);

        if (Tab == "plugins") RepoResults = _wp.SearchRepo(WpAssetType.Plugin, Q);
        else if (Tab == "themes") RepoResults = _wp.SearchRepo(WpAssetType.Theme, Q);
        return true;
    }

    public async Task<IActionResult> OnGetAsync(int id) => await LoadAsync(id) ? Page() : NotFound();

    private async Task<bool> OwnsAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        return user != null && await _installer.GetInstallationDetailsAsync(user.Id, id) != null;
    }

    public async Task<IActionResult> OnPostToggleAsync(int id, int assetId, bool active)
    {
        if (!await OwnsAsync(id)) return Forbid();
        await _wp.SetActiveAsync(id, assetId, active);
        return RedirectToPage(new { id, tab = "plugins" });
    }

    public async Task<IActionResult> OnPostDeleteAssetAsync(int id, int assetId, string tab)
    {
        if (!await OwnsAsync(id)) return Forbid();
        await _wp.DeleteAssetAsync(id, assetId);
        return RedirectToPage(new { id, tab });
    }

    public async Task<IActionResult> OnPostUpdateAssetAsync(int id, int assetId, string tab)
    {
        if (!await OwnsAsync(id)) return Forbid();
        await _wp.UpdateAssetAsync(id, assetId);
        return RedirectToPage(new { id, tab });
    }

    public async Task<IActionResult> OnPostUpdateAllAsync(int id)
    {
        if (!await OwnsAsync(id)) return Forbid();
        var n = await _wp.UpdateAllAsync(id);
        TempData["Success"] = n > 0 ? $"{n} item(s) updated." : "Everything is already up to date.";
        return RedirectToPage(new { id, tab = "plugins" });
    }

    public async Task<IActionResult> OnPostInstallAsync(int id, WpAssetType type, string slug, bool activate)
    {
        if (!await OwnsAsync(id)) return Forbid();
        await _wp.InstallFromRepoAsync(id, type, slug, activate);
        TempData["Success"] = $"{slug} installed{(activate ? " and activated" : "")}.";
        return RedirectToPage(new { id, tab = type == WpAssetType.Plugin ? "plugins" : "themes" });
    }

    public async Task<IActionResult> OnPostActivateThemeAsync(int id, int assetId)
    {
        if (!await OwnsAsync(id)) return Forbid();
        await _wp.ActivateThemeAsync(id, assetId);
        return RedirectToPage(new { id, tab = "themes" });
    }

    public async Task<IActionResult> OnPostCliAsync(int id, WpCliCommand command, string? argument)
    {
        if (!await OwnsAsync(id)) return Forbid();
        TempData["Success"] = await _wp.RunCliAsync(id, command, argument);
        return RedirectToPage(new { id, tab = "settings" });
    }

    public async Task<IActionResult> OnPostFlagAsync(int id, string flag, bool value)
    {
        if (!await OwnsAsync(id)) return Forbid();
        await _wp.SetFlagAsync(id, flag, value);
        TempData["Success"] = $"{flag} set to {value}.";
        return RedirectToPage(new { id, tab = "settings" });
    }
}
