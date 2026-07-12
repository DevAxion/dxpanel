using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Developer;

namespace SRXPanel.Pages.Client.Developer;

public class StagingModel : PageModel
{
    private readonly IStagingService _staging;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _auditLog;

    public StagingModel(IStagingService staging, ApplicationDbContext db,
        UserManager<ApplicationUser> userManager, IAuditLogService auditLog)
    {
        _staging = staging;
        _db = db;
        _userManager = userManager;
        _auditLog = auditLog;
    }

    public List<StagingSite> Sites { get; private set; } = new();

    /// <summary>Domains that do not have a staging site yet.</summary>
    public List<Domain> AvailableDomains { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        await LoadAsync(user.Id);
        return Page();
    }

    private async Task LoadAsync(string userId)
    {
        Sites = await _staging.GetSitesAsync(userId);
        var staged = Sites.Select(s => s.DomainId).ToHashSet();

        AvailableDomains = await _db.Domains
            .Where(d => d.UserId == userId)
            .OrderBy(d => d.DomainName)
            .ToListAsync();
        AvailableDomains = AvailableDomains.Where(d => !staged.Contains(d.Id)).ToList();
    }

    public async Task<IActionResult> OnPostCreateAsync(int domainId, bool cloneDatabase, bool passwordProtect,
        string? authUser, string? authPassword, int? expiryDays)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        try
        {
            var result = await _staging.CreateAsync(user.Id, domainId,
                new StagingOptions(cloneDatabase, passwordProtect, authUser, authPassword, expiryDays));

            await _auditLog.LogAsync("Create", "StagingSite", domainId.ToString(), result.Message);

            if (result.Success) TempData["Success"] = result.Message + (result.Simulated ? " (simulated)" : "");
            else TempData["Error"] = result.Message;
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRefreshAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        try
        {
            var result = await _staging.RefreshAsync(user.Id, id);
            TempData["Success"] = result.Message + (result.Simulated ? " (simulated)" : "");
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPushAsync(int id, bool syncFiles, bool syncDatabase,
        bool excludeUploads, bool clearCaches)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        try
        {
            var result = await _staging.PushToProductionAsync(user.Id, id,
                new PushOptions(syncFiles, syncDatabase, excludeUploads, clearCaches));

            await _auditLog.LogAsync("Push", "StagingSite", id.ToString(), "staging → production");
            TempData["Success"] = result.Message + (result.Simulated ? " (simulated)" : "");
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var result = await _staging.DeleteAsync(user.Id, id);
        await _auditLog.LogAsync("Delete", "StagingSite", id.ToString(), "");
        TempData["Success"] = result.Message + (result.Simulated ? " (simulated)" : "");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostProtectAsync(int id, bool enabled, string? authUser, string? authPassword)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        try
        {
            await _staging.SetPasswordProtectionAsync(user.Id, id, enabled, authUser, authPassword);
            TempData["Success"] = enabled ? "Staging site is now password protected." : "Password protection removed.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostExpiryAsync(int id, int? days)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        await _staging.SetExpiryAsync(user.Id, id, days);
        TempData["Success"] = days is int d and > 0
            ? $"Staging site will be deleted in {d} days."
            : "Expiry removed — the staging site will not be deleted automatically.";
        return RedirectToPage();
    }
}
