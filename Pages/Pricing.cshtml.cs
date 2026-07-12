using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Pages;

[AllowAnonymous]
public class PricingModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public PricingModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public List<Plan> Plans { get; set; } = new();
    public List<VpsPlan> VpsPlans { get; set; } = new();
    public List<ResellerPackage> ResellerPackages { get; set; } = new();

    public async Task OnGetAsync()
    {
        Plans = (await _db.Plans
            .Where(p => p.IsActive && p.BillingCycle == BillingCycle.Monthly)
            .ToListAsync())
            .OrderBy(p => p.Price)
            .ToList();

        VpsPlans = (await _db.VpsPlans.Where(v => v.IsActive).ToListAsync())
            .OrderBy(v => v.SortOrder).ThenBy(v => v.Price).ToList();

        ResellerPackages = (await _db.ResellerPackages.Where(r => r.IsPublic && r.IsActive).ToListAsync())
            .OrderBy(r => r.Price).ToList();
    }
}
