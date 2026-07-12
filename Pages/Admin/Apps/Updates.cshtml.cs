using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Apps;

namespace SRXPanel.Pages.Admin.Apps;

public class UpdatesModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IAppInstallerService _installer;

    public UpdatesModel(ApplicationDbContext db, IAppInstallerService installer)
    {
        _db = db;
        _installer = installer;
    }

    public List<AppInstallation> Installations { get; private set; } = new();
    public int UpdateAvailableCount { get; private set; }
    public AppUpdateSettings Settings { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Installations = await _db.AppInstallations
            .Include(i => i.AppDefinition).Include(i => i.Domain).Include(i => i.User)
            .OrderByDescending(i => i.Status == AppInstallStatus.UpdateAvailable)
            .ThenBy(i => i.SiteTitle)
            .Take(200).ToListAsync();
        UpdateAvailableCount = Installations.Count(i => i.Status == AppInstallStatus.UpdateAvailable);
        Settings = await _db.AppUpdateSettings.FirstOrDefaultAsync(s => s.Id == 1) ?? new AppUpdateSettings();
    }

    /// <summary>Compares each installation's version against its catalogue version.</summary>
    public async Task<IActionResult> OnPostCheckAsync()
    {
        var all = await _db.AppInstallations.Include(i => i.AppDefinition).ToListAsync();
        var found = 0;
        foreach (var i in all)
        {
            var latest = i.AppDefinition?.Version;
            if (string.IsNullOrEmpty(latest)) continue;

            if (!string.Equals(latest, i.InstalledVersion, StringComparison.Ordinal))
            {
                i.Status = AppInstallStatus.UpdateAvailable;
                i.AvailableVersion = latest;
                found++;
            }
            else if (i.Status == AppInstallStatus.UpdateAvailable)
            {
                i.Status = AppInstallStatus.Active;
                i.AvailableVersion = null;
            }
        }
        await _db.SaveChangesAsync();
        TempData["Success"] = found > 0 ? $"{found} installation(s) have updates available." : "All installations are up to date.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostBulkUpdateAsync()
    {
        var pending = await _db.AppInstallations
            .Where(i => i.Status == AppInstallStatus.UpdateAvailable).ToListAsync();

        foreach (var i in pending)
            await _installer.UpdateAsync(i.UserId, i.Id);

        TempData["Success"] = pending.Count > 0
            ? $"Queued {pending.Count} update job(s). Each site is backed up before updating."
            : "Nothing to update.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSettingsAsync(bool autoUpdateMinor, bool notifyMajorOnly,
        string schedule, bool emailClientOnUpdate, int keepRestorePoints)
    {
        var s = await _db.AppUpdateSettings.FirstOrDefaultAsync(x => x.Id == 1);
        if (s != null)
        {
            s.AutoUpdateMinor = autoUpdateMinor;
            s.NotifyMajorOnly = notifyMajorOnly;
            s.Schedule = schedule is "nightly" or "weekly" ? schedule : "nightly";
            s.EmailClientOnUpdate = emailClientOnUpdate;
            s.KeepRestorePoints = Math.Clamp(keepRestorePoints, 1, 10);
            s.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            TempData["Success"] = "Auto-update policy saved.";
        }
        return RedirectToPage();
    }
}
