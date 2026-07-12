using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services;

namespace SRXPanel.Pages.Admin;

public class UpdatesModel : PageModel
{
    private readonly IUpdateService _updates;
    private readonly IAuditLogService _audit;

    public UpdatesModel(IUpdateService updates, IAuditLogService audit)
    {
        _updates = updates;
        _audit = audit;
    }

    public UpdateCheckResult Check { get; private set; } = null!;
    public List<UpdateHistory> History { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Check = await _updates.CheckForUpdatesAsync();
        History = await _updates.GetHistoryAsync();
    }

    public async Task<IActionResult> OnPostApplyAsync(string version)
    {
        var check = await _updates.CheckForUpdatesAsync();
        if (!check.UpdateAvailable || check.Latest.Version != version)
        {
            TempData["Error"] = "That version is not available to install.";
            return RedirectToPage();
        }

        var record = await _updates.ApplyUpdateAsync(version, User.Identity?.Name);
        await _audit.LogAsync("Update", "Panel", version, $"Updated {record.FromVersion} → {record.ToVersion}");
        TempData["Success"] = record.Simulated
            ? $"Update to v{version} recorded (simulated on this host). Run scripts/update.sh on the server to apply."
            : $"Panel updated to v{version}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAutoAsync(bool enabled)
    {
        await _updates.SetAutoUpdateAsync(enabled);
        await _audit.LogAsync("Update", "PanelSettings", "auto-update", $"Automatic updates {(enabled ? "enabled" : "disabled")}");
        TempData["Success"] = $"Automatic updates {(enabled ? "enabled" : "disabled")}.";
        return RedirectToPage();
    }
}
