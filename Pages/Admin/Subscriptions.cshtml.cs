using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Billing;

namespace SRXPanel.Pages.Admin;

public class SubscriptionsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IProvisioningService _provisioning;
    private readonly IStripeGateway _stripe;
    private readonly IAuditLogService _audit;

    public SubscriptionsModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        IProvisioningService provisioning, IStripeGateway stripe, IAuditLogService audit)
    {
        _db = db;
        _userManager = userManager;
        _provisioning = provisioning;
        _stripe = stripe;
        _audit = audit;
    }

    public List<Subscription> Subscriptions { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? StatusFilter { get; set; }

    public string StripeDashboard(string? customerId) => _stripe.CustomerDashboardUrl(customerId);

    public async Task OnGetAsync()
    {
        var query = _db.Subscriptions.Include(s => s.User).Include(s => s.Plan).AsQueryable();
        if (Enum.TryParse<SubscriptionStatus>(StatusFilter, out var status))
        {
            query = query.Where(s => s.Status == status);
        }
        Subscriptions = await query.OrderByDescending(s => s.CreatedAt).ToListAsync();
    }

    public async Task<IActionResult> OnPostSuspendAsync(int id)
    {
        var sub = await _db.Subscriptions.Include(s => s.User).FirstOrDefaultAsync(s => s.Id == id);
        if (sub?.User == null) { TempData["Error"] = "Not found."; return RedirectToPage(); }

        sub.Status = SubscriptionStatus.PastDue;
        sub.PastDueSince ??= DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _provisioning.SuspendAsync(sub.User, "Suspended by administrator.");
        await _audit.LogAsync("Suspend", "Subscription", id.ToString(), sub.User.UserName);
        TempData["Success"] = $"Subscription for '{sub.User.UserName}' suspended.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostActivateAsync(int id)
    {
        var sub = await _db.Subscriptions.Include(s => s.User).Include(s => s.Plan).FirstOrDefaultAsync(s => s.Id == id);
        if (sub?.User == null) { TempData["Error"] = "Not found."; return RedirectToPage(); }

        sub.Status = SubscriptionStatus.Active;
        sub.PastDueSince = null;
        await _db.SaveChangesAsync();
        await _provisioning.ReactivateAsync(sub.User);
        await _audit.LogAsync("Activate", "Subscription", id.ToString(), sub.User.UserName);
        TempData["Success"] = $"Subscription for '{sub.User.UserName}' activated.";
        return RedirectToPage();
    }
}
