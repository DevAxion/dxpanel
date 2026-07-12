using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Apps;

namespace SRXPanel.Pages.Client.Apps;

public class ManageModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IAppInstallerService _installer;
    private readonly UserManager<ApplicationUser> _userManager;

    public ManageModel(ApplicationDbContext db, IAppInstallerService installer, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _installer = installer;
        _userManager = userManager;
    }

    public AppInstallation Installation { get; private set; } = null!;
    public List<Domain> Domains { get; private set; } = new();
    public List<Backup> RestorePoints { get; private set; } = new();

    private async Task<bool> LoadAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return false;

        var inst = await _installer.GetInstallationDetailsAsync(user.Id, id);
        if (inst == null) return false;
        Installation = inst;

        Domains = await _db.Domains.Where(d => d.UserId == user.Id).OrderBy(d => d.DomainName).ToListAsync();
        RestorePoints = await _installer.GetRestorePointsAsync(user.Id, id);
        return true;
    }

    public async Task<IActionResult> OnGetAsync(int id) => await LoadAsync(id) ? Page() : NotFound();

    public async Task<IActionResult> OnPostUpdateAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var jobId = await _installer.UpdateAsync(user.Id, id);
        return RedirectToPage("/Client/Apps/Progress", new { jobId });
    }

    public async Task<IActionResult> OnPostCloneAsync(int id, int targetDomainId, string path)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var jobId = await _installer.CloneAsync(user.Id, id, targetDomainId, string.IsNullOrWhiteSpace(path) ? "/staging" : path);
        return RedirectToPage("/Client/Apps/Progress", new { jobId });
    }

    public async Task<IActionResult> OnPostUninstallAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var jobId = await _installer.UninstallAsync(user.Id, id);
        return RedirectToPage("/Client/Apps/Progress", new { jobId });
    }

    public async Task<IActionResult> OnPostChangePasswordAsync(int id, string newPassword)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            TempData["Error"] = "Password must be at least 8 characters.";
            return RedirectToPage(new { id });
        }
        await _installer.ChangeAdminPasswordAsync(user.Id, id, newPassword);
        TempData["Success"] = "Application admin password updated.";
        return RedirectToPage(new { id });
    }
}
