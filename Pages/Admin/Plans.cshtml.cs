using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Billing;

namespace SRXPanel.Pages.Admin;

public class PlansModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IStripeGateway _stripe;
    private readonly IAuditLogService _audit;

    public PlansModel(ApplicationDbContext db, IStripeGateway stripe, IAuditLogService audit)
    {
        _db = db;
        _stripe = stripe;
        _audit = audit;
    }

    public List<Plan> Plans { get; set; } = new();

    [BindProperty]
    public PlanInput Input { get; set; } = new();

    public class PlanInput
    {
        public int Id { get; set; }
        [Required] [StringLength(100)] public string Name { get; set; } = string.Empty;
        [StringLength(500)] public string Description { get; set; } = string.Empty;
        [Range(0, 100000)] public decimal Price { get; set; }
        public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;
        public long DiskQuotaMB { get; set; }
        public long BandwidthQuotaMB { get; set; }
        public int MaxDomains { get; set; }
        public int MaxEmails { get; set; }
        public int MaxDatabases { get; set; }
        public int MaxFtpAccounts { get; set; }
    }

    public async Task OnGetAsync()
    {
        Plans = (await _db.Plans.ToListAsync()).OrderBy(p => p.Price).ToList();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!ModelState.IsValid)
        {
            Plans = (await _db.Plans.ToListAsync()).OrderBy(p => p.Price).ToList();
            return Page();
        }

        Plan plan;
        if (Input.Id > 0)
        {
            plan = await _db.Plans.FindAsync(Input.Id) ?? new Plan();
        }
        else
        {
            plan = new Plan { Currency = _stripe.SimulationMode ? "usd" : "usd" };
            _db.Plans.Add(plan);
        }

        plan.Name = Input.Name;
        plan.Description = Input.Description;
        plan.Price = Input.Price;
        plan.BillingCycle = Input.BillingCycle;
        plan.DiskQuotaMB = Input.DiskQuotaMB;
        plan.BandwidthQuotaMB = Input.BandwidthQuotaMB;
        plan.MaxDomains = Input.MaxDomains;
        plan.MaxEmails = Input.MaxEmails;
        plan.MaxDatabases = Input.MaxDatabases;
        plan.MaxFtpAccounts = Input.MaxFtpAccounts;

        await _db.SaveChangesAsync();
        await _audit.LogAsync(Input.Id > 0 ? "Update" : "Create", "Plan", plan.Id.ToString(), plan.Name);
        TempData["Success"] = $"Plan '{plan.Name}' saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSyncAsync(int id)
    {
        var plan = await _db.Plans.FindAsync(id);
        if (plan == null) { TempData["Error"] = "Plan not found."; return RedirectToPage(); }

        var (productId, priceId) = await _stripe.SyncPlanAsync(plan);
        plan.StripeProductId = productId;
        plan.StripePriceId = priceId;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("StripeSync", "Plan", plan.Id.ToString(), plan.Name);

        TempData["Success"] = _stripe.SimulationMode
            ? $"Plan '{plan.Name}' synced to Stripe (simulated: {productId} / {priceId})."
            : $"Plan '{plan.Name}' synced to Stripe.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var plan = await _db.Plans.FindAsync(id);
        if (plan == null) { TempData["Error"] = "Plan not found."; return RedirectToPage(); }
        plan.IsActive = !plan.IsActive;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(plan.IsActive ? "Activate" : "Deactivate", "Plan", plan.Id.ToString(), plan.Name);
        TempData["Success"] = $"Plan '{plan.Name}' {(plan.IsActive ? "activated" : "deactivated")}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var plan = await _db.Plans.FindAsync(id);
        if (plan == null) { TempData["Error"] = "Plan not found."; return RedirectToPage(); }
        if (await _db.Subscriptions.AnyAsync(s => s.PlanId == id))
        {
            TempData["Error"] = "Cannot delete a plan that has subscriptions. Deactivate it instead.";
            return RedirectToPage();
        }
        _db.Plans.Remove(plan);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "Plan", id.ToString(), plan.Name);
        TempData["Success"] = $"Plan '{plan.Name}' deleted.";
        return RedirectToPage();
    }
}
