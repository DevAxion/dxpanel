using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Pages.Admin.Apps;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public List<AppDefinition> Apps { get; private set; } = new();
    public List<AppInstallJob> FailedJobs { get; private set; } = new();
    public int TotalInstallations { get; private set; }
    public int FailedCount { get; private set; }
    public List<(string Name, int Count)> Popular { get; private set; } = new();

    [BindProperty] public AppDefinition Input { get; set; } = new();
    public bool Editing => Input.Id != 0;

    public async Task OnGetAsync(int? editId)
    {
        if (editId.HasValue)
        {
            var a = await _db.AppDefinitions.FindAsync(editId.Value);
            if (a != null) Input = a;
        }
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        Apps = await _db.AppDefinitions.OrderBy(a => a.Category).ThenBy(a => a.Name).ToListAsync();
        TotalInstallations = await _db.AppInstallations.CountAsync();
        FailedJobs = await _db.AppInstallJobs.Where(j => j.Status == AppJobStatus.Failed)
            .OrderByDescending(j => j.StartedAt).Take(20).ToListAsync();
        FailedCount = FailedJobs.Count;

        Popular = (await _db.AppInstallations.Include(i => i.AppDefinition)
                .GroupBy(i => i.AppDefinition!.Name)
                .Select(g => new { g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count).Take(6).ToListAsync())
            .Select(x => (x.Key, x.Count)).ToList();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Input.Name) || string.IsNullOrWhiteSpace(Input.Slug))
        {
            ModelState.AddModelError(string.Empty, "Name and slug are required.");
            await LoadAsync();
            return Page();
        }

        if (Input.Id == 0)
        {
            if (await _db.AppDefinitions.AnyAsync(a => a.Slug == Input.Slug))
            {
                ModelState.AddModelError(string.Empty, "An app with that slug already exists.");
                await LoadAsync();
                return Page();
            }
            Input.CreatedAt = DateTime.UtcNow;
            _db.AppDefinitions.Add(Input);
        }
        else
        {
            var a = await _db.AppDefinitions.FindAsync(Input.Id);
            if (a == null) return RedirectToPage();
            a.Name = Input.Name;
            a.Slug = Input.Slug;
            a.Description = Input.Description;
            a.Category = Input.Category;
            a.Version = Input.Version;
            a.IconPath = Input.IconPath;
            a.MinDiskMB = Input.MinDiskMB;
            a.MinPhpVersion = Input.MinPhpVersion;
            a.DownloadUrl = Input.DownloadUrl;
            a.RequiresDatabase = Input.RequiresDatabase;
            a.IsActive = Input.IsActive;
            a.Features = Input.Features;
        }
        await _db.SaveChangesAsync();
        TempData["Success"] = "Application definition saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var a = await _db.AppDefinitions.FindAsync(id);
        if (a != null) { a.IsActive = !a.IsActive; await _db.SaveChangesAsync(); }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var a = await _db.AppDefinitions.FindAsync(id);
        if (a == null) return RedirectToPage();
        if (await _db.AppInstallations.AnyAsync(i => i.AppDefinitionId == id))
        {
            TempData["Error"] = "Cannot delete an app that still has installations. Disable it instead.";
            return RedirectToPage();
        }
        _db.AppDefinitions.Remove(a);
        await _db.SaveChangesAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostClearJobAsync(int jobId)
    {
        var j = await _db.AppInstallJobs.FindAsync(jobId);
        if (j != null) { _db.AppInstallJobs.Remove(j); await _db.SaveChangesAsync(); }
        return RedirectToPage();
    }
}
