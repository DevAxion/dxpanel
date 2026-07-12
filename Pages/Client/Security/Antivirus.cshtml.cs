using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Security;

namespace SRXPanel.Pages.Client.Security;

public class AntivirusModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IClamAvService _av;

    public AntivirusModel(UserManager<ApplicationUser> userManager, IClamAvService av)
    {
        _userManager = userManager;
        _av = av;
    }

    public List<ScanResult> Recent { get; private set; } = new();
    public List<QuarantinedFile> Quarantine { get; private set; } = new();
    public DateTime DefinitionsDate { get; private set; }
    public string HomePath { get; private set; } = "";
    public string UserId { get; private set; } = "";

    private string HomeFor(ApplicationUser u) => $"/home/{HostingHelpers.UserPrefix(u.UserName ?? "user")}/public_html";

    public async Task OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return;
        UserId = user.Id;
        HomePath = HomeFor(user);
        Recent = await _av.GetRecentScansAsync(user.Id);
        Quarantine = await _av.GetQuarantineAsync(user.Id);
        DefinitionsDate = await _av.GetDefinitionDateAsync();
    }

    public async Task<IActionResult> OnPostScanAsync(string? path)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var target = string.IsNullOrWhiteSpace(path) ? HomeFor(user) : path;
        var summary = await _av.ScanDirectoryAsync(user.Id, target);
        TempData["Success"] = $"Scan complete — {summary.Scanned} file(s) scanned, {summary.Infected} threat(s) found.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostQuarantineAsync(string path, string? threat)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        await _av.QuarantineFileAsync(user.Id, path, threat);
        TempData["Success"] = "File moved to quarantine.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRestoreAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        await _av.RestoreFileAsync(user.Id, id);
        TempData["Success"] = "File restored.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        await _av.DeleteQuarantinedAsync(user.Id, id);
        TempData["Success"] = "Quarantined file deleted.";
        return RedirectToPage();
    }
}
