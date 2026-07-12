using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Billing;

namespace SRXPanel.Pages.Billing;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IBillingService _billing;

    public IndexModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IBillingService billing)
    {
        _db = db;
        _userManager = userManager;
        _billing = billing;
    }

    public Subscription? Subscription { get; set; }
    public Plan? Plan { get; set; }
    public List<Invoice> Invoices { get; set; } = new();
    public List<PaymentMethod> PaymentMethods { get; set; } = new();
    public List<Plan> AvailablePlans { get; set; } = new();

    // Usage
    public int DomainCount { get; set; }
    public int DatabaseCount { get; set; }
    public int EmailCount { get; set; }
    public int FtpCount { get; set; }

    public static string Money(decimal amount, string currency) => BillingService.FormatMoney(amount, currency);

    private async Task<ApplicationUser?> LoadAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return null;

        Subscription = await _db.Subscriptions.Include(s => s.Plan)
            .Where(s => s.UserId == user.Id)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();
        Plan = Subscription?.Plan;

        Invoices = await _db.Invoices.Where(i => i.UserId == user.Id)
            .OrderByDescending(i => i.CreatedAt).Take(50).ToListAsync();
        PaymentMethods = await _db.PaymentMethods.Where(p => p.UserId == user.Id)
            .OrderByDescending(p => p.IsDefault).ToListAsync();
        AvailablePlans = (await _db.Plans.Where(p => p.IsActive).ToListAsync()).OrderBy(p => p.Price).ToList();

        DomainCount = await _db.Domains.CountAsync(d => d.UserId == user.Id);
        DatabaseCount = await _db.Databases.CountAsync(d => d.UserId == user.Id);
        EmailCount = await _db.EmailAccounts.CountAsync(e => e.UserId == user.Id);
        FtpCount = await _db.FtpAccounts.CountAsync(f => f.UserId == user.Id);
        return user;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await LoadAsync();
        if (user == null) return Challenge();
        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var sub = await _db.Subscriptions.Where(s => s.UserId == user.Id && s.Status != SubscriptionStatus.Cancelled)
            .OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync();
        if (sub == null)
        {
            TempData["Error"] = "No active subscription to cancel.";
            return RedirectToPage();
        }

        await _billing.CancelAsync(sub);
        TempData["Success"] = $"Subscription cancelled. Your service remains active until {sub.CurrentPeriodEnd:yyyy-MM-dd}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostChangePlanAsync(int newPlanId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var sub = await _db.Subscriptions.Where(s => s.UserId == user.Id && s.Status != SubscriptionStatus.Cancelled)
            .OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync();
        if (sub == null)
        {
            TempData["Error"] = "No active subscription to change.";
            return RedirectToPage();
        }

        await _billing.HandleSubscriptionUpdatedAsync(sub.StripeSubscriptionId, newPlanId);
        // Sim subscriptions may lack a Stripe id — apply directly too.
        if (string.IsNullOrEmpty(sub.StripeSubscriptionId))
        {
            var plan = await _db.Plans.FindAsync(newPlanId);
            if (plan != null)
            {
                sub.PlanId = plan.Id;
                user.DiskQuotaMB = plan.DiskQuotaMB;
                user.BandwidthQuotaMB = plan.BandwidthQuotaMB;
                await _userManager.UpdateAsync(user);
                await _db.SaveChangesAsync();
            }
        }
        TempData["Success"] = "Your plan has been updated.";
        return RedirectToPage();
    }
}
