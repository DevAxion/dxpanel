using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Email;

namespace SRXPanel.Pages.Admin.Email;

public class BlacklistModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IBlacklistService _blacklist;

    public BlacklistModel(ApplicationDbContext db, IBlacklistService blacklist)
    {
        _db = db;
        _blacklist = blacklist;
    }

    public List<BlacklistEntry> Listed { get; private set; } = new();
    public List<BlacklistCheck> Recent { get; private set; } = new();
    public int TotalChecks { get; private set; }

    public async Task OnGetAsync()
    {
        Listed = await _db.BlacklistEntries.Include(e => e.Domain)
            .Where(e => e.IsListed && !e.IsResolved)
            .OrderByDescending(e => e.FirstDetectedAt).ToListAsync();
        Recent = await _db.BlacklistChecks.Include(c => c.Domain)
            .OrderByDescending(c => c.CheckedAt).Take(30).ToListAsync();
        TotalChecks = await _db.BlacklistChecks.CountAsync();
    }

    public async Task<IActionResult> OnPostCheckAllAsync()
    {
        // Run a check for every domain that has a mailbox or mail config.
        var domainIds = await _db.Domains.Select(d => d.Id).ToListAsync();
        var checkedCount = 0;
        foreach (var id in domainIds.Take(50)) // cap the batch
        {
            var domain = await _db.Domains.FirstAsync(d => d.Id == id);
            await _blacklist.CheckAllAsync(id, domain.UserId);
            checkedCount++;
        }
        TempData["Success"] = $"Ran blacklist checks for {checkedCount} domain(s).";
        return RedirectToPage();
    }
}
