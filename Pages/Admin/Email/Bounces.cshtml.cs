using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Email;

namespace SRXPanel.Pages.Admin.Email;

public class BouncesModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IBounceHandlerService _bounces;

    public BouncesModel(ApplicationDbContext db, IBounceHandlerService bounces)
    {
        _db = db;
        _bounces = bounces;
    }

    public int TotalHard { get; private set; }
    public int TotalSoft { get; private set; }
    public int TotalSuppressed { get; private set; }
    public List<EmailBounce> Recent { get; private set; } = new();
    public List<(string domain, int hard, double rate)> HighBounceDomains { get; private set; } = new();

    public async Task OnGetAsync()
    {
        TotalHard = await _db.EmailBounces.CountAsync(b => b.BounceType == BounceType.Hard);
        TotalSoft = await _db.EmailBounces.CountAsync(b => b.BounceType == BounceType.Soft);
        TotalSuppressed = await _db.EmailBounces.CountAsync(b => b.IsBlacklisted);

        Recent = await _db.EmailBounces.Include(b => b.Domain)
            .OrderByDescending(b => b.OccurredAt).Take(30).ToListAsync();

        // Domains with the most hard bounces (proxy for high bounce-rate users).
        var raw = await _db.EmailBounces
            .Where(b => b.BounceType == BounceType.Hard && b.Domain != null)
            .GroupBy(b => b.Domain!.DomainName)
            .Select(g => new { Domain = g.Key, Hard = g.Count() })
            .OrderByDescending(x => x.Hard).Take(10).ToListAsync();
        HighBounceDomains = raw.Select(x => (x.Domain, x.Hard, (double)x.Hard)).ToList();
    }

    public async Task<IActionResult> OnPostBlacklistAllAsync(int domainId)
    {
        var n = await _bounces.BlacklistBouncedAsync(domainId);
        TempData["Success"] = $"{n} hard-bounced address(es) suppressed for domain {domainId}.";
        return RedirectToPage();
    }
}
