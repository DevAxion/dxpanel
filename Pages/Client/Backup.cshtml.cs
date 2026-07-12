using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Integration;
using SRXPanel.Services.Portal;

namespace SRXPanel.Pages.Client;

public class BackupModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IBackupService _backups;
    private readonly IOffSiteBackupService _offsite;

    public BackupModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        IBackupService backups, IOffSiteBackupService offsite)
    {
        _db = db;
        _userManager = userManager;
        _backups = backups;
        _offsite = offsite;
    }

    /// <summary>Whether the operator has configured an off-site backup destination.</summary>
    public bool OffsiteEnabled => _offsite.IsConfigured;
    public string OffsiteProvider => _offsite.Provider;

    public List<Models.Backup> Backups { get; set; } = new();
    public BackupSchedule Schedule { get; set; } = new();

    /// <summary>Max backups allowed by the user's plan (0 = unlimited).</summary>
    public int BackupLimit { get; set; }
    public bool LimitReached { get; set; }

    private async Task<ApplicationUser?> LoadAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return null;
        Backups = await _db.Backups.Where(b => b.UserId == user.Id).OrderByDescending(b => b.CreatedAt).ToListAsync();
        Schedule = await _backups.GetOrCreateScheduleAsync(user.Id);
        BackupLimit = await _backups.GetBackupLimitAsync(user.Id);
        LimitReached = BackupLimit > 0 && Backups.Count >= BackupLimit;
        return user;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (await LoadAsync() == null) return Challenge();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(BackupType type)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        // Plan-based limit: block creation once the plan's backup quota is reached.
        if (!await _backups.CanCreateBackupAsync(user.Id))
        {
            var limit = await _backups.GetBackupLimitAsync(user.Id);
            TempData["Error"] = $"You have reached your plan's backup limit ({limit}). " +
                "Upgrade your plan to get more backups, or delete an existing backup first.";
            return RedirectToPage();
        }

        await _backups.CreateBackupAsync(user.Id, user.UserName ?? "user", type);
        var schedule = await _backups.GetOrCreateScheduleAsync(user.Id);
        await _backups.EnforceRetentionAsync(user.Id, schedule.Retention);
        TempData["Success"] = $"{type} backup created.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostScheduleAsync(BackupFrequency frequency, int retention, string destination, string? s3Bucket, bool enabled)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var schedule = await _backups.GetOrCreateScheduleAsync(user.Id);
        schedule.Frequency = frequency;
        schedule.Retention = Math.Clamp(retention, 1, 100);
        schedule.Destination = destination == "s3" ? "s3" : "local";
        schedule.S3Bucket = s3Bucket;
        schedule.IsEnabled = enabled;
        await _backups.SaveScheduleAsync(schedule);
        TempData["Success"] = "Backup schedule saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRestoreAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var result = await _backups.RestoreAsync(user.Id, id);
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDownloadAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var backup = await _db.Backups.FirstOrDefaultAsync(b => b.Id == id && b.UserId == user.Id);
        if (backup?.FilePath == null || !System.IO.File.Exists(backup.FilePath))
        {
            TempData["Error"] = "Backup file not available.";
            return RedirectToPage();
        }
        var bytes = await System.IO.File.ReadAllBytesAsync(backup.FilePath);
        return File(bytes, "application/octet-stream", Path.GetFileName(backup.FilePath));
    }

    public async Task<IActionResult> OnPostOffsiteUploadAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var backup = await _db.Backups.FirstOrDefaultAsync(b => b.Id == id && b.UserId == user.Id);
        if (backup?.FilePath == null || !System.IO.File.Exists(backup.FilePath))
        {
            TempData["Error"] = "Backup file not available to upload.";
            return RedirectToPage();
        }

        var fileName = Path.GetFileName(backup.FilePath);
        var result = await _offsite.UploadBackupAsync(backup.FilePath, fileName);
        if (result.Success)
        {
            backup.OffsiteStored = true;
            backup.OffsiteUploadedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostOffsiteRemoveAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var backup = await _db.Backups.FirstOrDefaultAsync(b => b.Id == id && b.UserId == user.Id);
        if (backup?.FilePath == null) { TempData["Error"] = "Not found."; return RedirectToPage(); }

        var result = await _offsite.DeleteRemoteBackupAsync(Path.GetFileName(backup.FilePath));
        if (result.Success)
        {
            backup.OffsiteStored = false;
            backup.OffsiteUploadedAt = null;
            await _db.SaveChangesAsync();
        }
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var backup = await _db.Backups.FirstOrDefaultAsync(b => b.Id == id && b.UserId == user.Id);
        if (backup == null) { TempData["Error"] = "Not found."; return RedirectToPage(); }
        try { if (backup.FilePath != null && System.IO.File.Exists(backup.FilePath)) System.IO.File.Delete(backup.FilePath); } catch { }
        _db.Backups.Remove(backup);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Backup deleted.";
        return RedirectToPage();
    }

    public static string Size(long bytes)
    {
        string[] u = { "B", "KB", "MB", "GB" };
        double s = bytes; int i = 0;
        while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
        return $"{s:0.#} {u[i]}";
    }
}
