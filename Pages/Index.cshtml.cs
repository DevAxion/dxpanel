using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Pages;

[AllowAnonymous]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public List<Plan> Plans { get; private set; } = new();
    public List<FeatureItem> Features { get; private set; } = new();
    public List<StatCounter> Stats { get; private set; } = new();
    public List<Testimonial> Testimonials { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        // Logged-in users go straight to their dashboard; everyone else sees the
        // public marketing landing page.
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToPage("/Dashboard/Index");
        }

        // SQLite can't ORDER BY a decimal column, so sort client-side.
        var active = await _db.Plans.Where(p => p.IsActive && p.BillingCycle == BillingCycle.Monthly).ToListAsync();
        Plans = active.OrderBy(p => p.Price).Take(3).ToList();

        Features = await _db.FeatureItems.Where(f => f.IsPublished).OrderBy(f => f.SortOrder).ToListAsync();
        Stats = await _db.StatCounters.OrderBy(s => s.SortOrder).ToListAsync();
        Testimonials = await _db.Testimonials.Where(t => t.IsPublished).OrderBy(t => t.SortOrder).ToListAsync();

        return Page();
    }
}
