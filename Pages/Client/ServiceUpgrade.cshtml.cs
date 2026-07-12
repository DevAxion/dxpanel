using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Store;

namespace SRXPanel.Pages.Client;

public class ServiceUpgradeModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IStoreService _store;

    public ServiceUpgradeModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IStoreService store)
    {
        _db = db;
        _userManager = userManager;
        _store = store;
    }

    public Subscription Subscription { get; private set; } = null!;
    public Plan CurrentPlan { get; private set; } = null!;
    public List<UpgradeQuote> Options { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var sub = await _db.Subscriptions.Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == user.Id);
        if (sub?.Plan == null) return RedirectToPage("/Client/Services");
        Subscription = sub;
        CurrentPlan = sub.Plan;

        var plans = await _db.Plans.Where(p => p.IsActive && p.BillingCycle == BillingCycle.Monthly && p.Id != sub.PlanId).ToListAsync();
        foreach (var p in plans)
        {
            var q = await _store.QuoteUpgradeAsync(sub.Id, p.Id);
            if (q != null) Options.Add(q);
        }
        Options = Options.OrderBy(o => o.Target.Price).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostApplyAsync(int id, int newPlanId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var (ok, message) = await _store.ApplyPlanChangeAsync(user, id, newPlanId);
        TempData[ok ? "Success" : "Error"] = message;
        return RedirectToPage("/Client/Services");
    }
}
