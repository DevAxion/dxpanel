using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Billing;
using SRXPanel.Services.Reseller;

namespace SRXPanel.Pages;

[AllowAnonymous]
public class RegisterModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IAuditLogService _audit;
    private readonly IAffiliateService _affiliates;
    private readonly IBillingService _billing;

    public RegisterModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager, IAuditLogService audit,
        IAffiliateService affiliates, IBillingService billing)
    {
        _db = db;
        _userManager = userManager;
        _signInManager = signInManager;
        _audit = audit;
        _affiliates = affiliates;
        _billing = billing;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty(SupportsGet = true)] public int? Plan { get; set; }

    public List<Plan> Plans { get; private set; } = new();

    public class InputModel
    {
        [Required, StringLength(120)] public string FullName { get; set; } = string.Empty;
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;

        [Required, StringLength(64, MinimumLength = 3)]
        [RegularExpression("^[a-zA-Z0-9_.-]+$", ErrorMessage = "Letters, numbers, and . _ - only.")]
        public string UserName { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public int? PlanId { get; set; }
        public string BillingChoice { get; set; } = "trial"; // trial | pay
    }

    private async Task LoadPlansAsync()
    {
        Plans = (await _db.Plans.Where(p => p.IsActive && p.BillingCycle == BillingCycle.Monthly).ToListAsync())
            .OrderBy(p => p.Price).ToList();
    }

    public async Task OnGetAsync()
    {
        await LoadPlansAsync();
        if (Plan.HasValue) Input.PlanId = Plan;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadPlansAsync();
        if (!ModelState.IsValid) return Page();

        var user = new ApplicationUser
        {
            UserName = Input.UserName,
            Email = Input.Email,
            FullName = Input.FullName,
            EmailConfirmed = true,
            IsActive = true,
            DiskQuotaMB = 0,
            BandwidthQuotaMB = 0,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, Input.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return Page();
        }

        await _userManager.AddToRoleAsync(user, Roles.Client);
        await _audit.LogAsync("Register", "User", user.Id, user.UserName);

        // Affiliate attribution via referral cookie.
        var refCode = Request.Cookies["srx_ref"];
        if (!string.IsNullOrEmpty(refCode))
        {
            var affiliate = await _affiliates.GetByCodeAsync(refCode);
            if (affiliate != null && affiliate.UserId != user.Id)
            {
                var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                await _affiliates.RecordSignupAsync(affiliate.Id, user.Id, ip);
                user.ReferredByAffiliateId = affiliate.Id;
                await _userManager.UpdateAsync(user);
            }
            Response.Cookies.Delete("srx_ref");
        }

        await _signInManager.SignInAsync(user, isPersistent: false);

        // Plan selection: trial starts immediately; paid goes to the checkout page.
        if (Input.PlanId.HasValue)
        {
            var plan = await _db.Plans.FindAsync(Input.PlanId.Value);
            if (plan != null && plan.IsActive)
            {
                if (Input.BillingChoice == "pay")
                {
                    return RedirectToPage("/Checkout/Index", new { planId = plan.Id });
                }

                await _billing.StartTrialAsync(user, plan);
                TempData["Success"] = $"Welcome aboard! Your 14-day free trial of {plan.Name} has started. 🎉";
                return RedirectToPage("/Dashboard/Index");
            }
        }

        TempData["Success"] = "Welcome to SRXPanel! Your account is ready.";
        return RedirectToPage("/Dashboard/Index");
    }
}
