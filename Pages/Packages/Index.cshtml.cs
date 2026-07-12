using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;

namespace SRXPanel.Pages.Packages;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditLogService _auditLog;

    public IndexModel(ApplicationDbContext db, IAuditLogService auditLog)
    {
        _db = db;
        _auditLog = auditLog;
    }

    public List<Package> Packages { get; set; } = new();

    public async Task OnGetAsync()
    {
        var packages = await _db.Packages.ToListAsync();
        Packages = packages.OrderBy(p => p.Price).ToList();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var package = await _db.Packages.Include(p => p.Users).FirstOrDefaultAsync(p => p.Id == id);
        if (package == null)
        {
            TempData["Error"] = "Package not found.";
            return RedirectToPage();
        }

        if (package.Users.Any())
        {
            TempData["Error"] = $"Cannot delete '{package.Name}' — it is assigned to {package.Users.Count} user(s).";
            return RedirectToPage();
        }

        var name = package.Name;
        _db.Packages.Remove(package);
        await _db.SaveChangesAsync();

        await _auditLog.LogAsync("Delete", "Package", id.ToString(), name);
        TempData["Success"] = $"Package '{name}' has been deleted.";
        return RedirectToPage();
    }
}
