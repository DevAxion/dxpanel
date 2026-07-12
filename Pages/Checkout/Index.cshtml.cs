using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Billing;

namespace SRXPanel.Pages.Checkout;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IBillingService _billing;
    private readonly IStripeGateway _stripe;

    public IndexModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        IBillingService billing, IStripeGateway stripe)
    {
        _db = db;
        _userManager = userManager;
        _billing = billing;
        _stripe = stripe;
    }

    [BindProperty(SupportsGet = true)]
    public int PlanId { get; set; }

    [BindProperty]
    public string? CouponCode { get; set; }

    [BindProperty]
    public string? PaymentMethodToken { get; set; }

    [BindProperty]
    public bool StartTrial { get; set; }

    public Plan Plan { get; set; } = null!;
    public bool SimulationMode => _stripe.SimulationMode;
    public string PublishableKey => _stripe.PublishableKey;
    public Coupon? AppliedCoupon { get; set; }
    public decimal FinalAmount { get; set; }

    private async Task<bool> LoadPlanAsync()
    {
        var plan = await _db.Plans.FindAsync(PlanId);
        if (plan == null || !plan.IsActive) return false;
        Plan = plan;
        AppliedCoupon = _billing.ValidateCoupon(CouponCode);
        FinalAmount = _billing.ApplyDiscount(plan.Price, AppliedCoupon);
        return true;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadPlanAsync()) return RedirectToPage("/Pricing");
        return Page();
    }

    public async Task<IActionResult> OnPostApplyCouponAsync()
    {
        if (!await LoadPlanAsync()) return RedirectToPage("/Pricing");
        if (!string.IsNullOrWhiteSpace(CouponCode) && AppliedCoupon == null)
        {
            TempData["Error"] = "Invalid or expired coupon code.";
        }
        else if (AppliedCoupon != null)
        {
            TempData["Success"] = $"Coupon applied — {AppliedCoupon.DiscountPercent}% off!";
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadPlanAsync()) return RedirectToPage("/Pricing");

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (StartTrial)
        {
            await _billing.StartTrialAsync(user, Plan);
            TempData["Success"] = $"Your 14-day free trial of {Plan.Name} has started! Welcome to SRXPanel.";
            return RedirectToPage("/Dashboard/Index");
        }

        await _billing.CheckoutAsync(user, Plan, PaymentMethodToken, CouponCode);
        TempData["Success"] = $"Payment successful! Your {Plan.Name} plan is now active. Welcome aboard! 🎉";
        return RedirectToPage("/Dashboard/Index");
    }
}
