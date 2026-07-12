using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Security;

namespace SRXPanel.Pages.Admin.Security;

public class AntivirusModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IClamAvService _av;

    public AntivirusModel(ApplicationDbContext db, IClamAvService av)
    {
        _db = db;
        _av = av;
    }

    public int TotalScans { get; private set; }
    public int InfectedCount { get; private set; }
    public int QuarantineCount { get; private set; }
    public DateTime DefinitionsDate { get; private set; }
    public SecuritySettings Settings { get; private set; } = new();
    public List<QuarantinedFile> Quarantine { get; private set; } = new();

    public async Task OnGetAsync()
    {
        TotalScans = await _db.ScanResults.CountAsync();
        InfectedCount = await _db.ScanResults.CountAsync(s => s.Status == ScanStatus.Infected);
        QuarantineCount = await _db.QuarantinedFiles.CountAsync(q => !q.IsDeleted && q.RestoredAt == null);
        DefinitionsDate = await _av.GetDefinitionDateAsync();
        Settings = await _db.SecuritySettings.FirstOrDefaultAsync(s => s.Id == 1) ?? new SecuritySettings();
        Quarantine = await _db.QuarantinedFiles.Where(q => !q.IsDeleted && q.RestoredAt == null)
            .OrderByDescending(q => q.QuarantinedAt).Take(50).ToListAsync();
    }

    public async Task<IActionResult> OnPostUpdateDefsAsync()
    {
        var date = await _av.UpdateDefinitionsAsync();
        TempData["Success"] = $"ClamAV definitions updated ({date:MMM d, yyyy}).";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostScheduleAsync(bool scheduleEnabled, string schedule, bool scanOnUpload)
    {
        var s = await _db.SecuritySettings.FirstOrDefaultAsync(x => x.Id == 1);
        if (s != null)
        {
            s.AvScheduleEnabled = scheduleEnabled;
            s.AvSchedule = schedule is "daily" or "weekly" ? schedule : "daily";
            s.AvScanOnUpload = scanOnUpload;
            s.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            TempData["Success"] = "Antivirus schedule saved.";
        }
        return RedirectToPage();
    }
}
