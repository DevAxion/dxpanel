using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Pages.Settings;

public class CommandLogModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public CommandLogModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public List<CommandLog> Logs { get; set; } = new();
    public List<string> Services { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ServiceFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Page { get; set; } = 1;

    public int TotalPages { get; set; }
    private const int PageSize = 50;

    public async Task OnGetAsync()
    {
        Services = await _db.CommandLogs
            .Where(c => c.Service != null)
            .Select(c => c.Service!)
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync();

        var query = _db.CommandLogs.AsQueryable();
        if (!string.IsNullOrEmpty(ServiceFilter))
        {
            query = query.Where(c => c.Service == ServiceFilter);
        }

        var total = await query.CountAsync();
        TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        Page = Math.Clamp(Page, 1, TotalPages);

        Logs = await query
            .OrderByDescending(c => c.ExecutedAt)
            .Skip((Page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostClearAsync()
    {
        await _db.CommandLogs.ExecuteDeleteAsync();
        TempData["Success"] = "Command log cleared.";
        return RedirectToPage();
    }
}
