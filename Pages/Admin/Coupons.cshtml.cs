using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Billing;

namespace SRXPanel.Pages.Admin;

public class CouponsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IStripeGateway _stripe;
    private readonly IAuditLogService _audit;

    public CouponsModel(ApplicationDbContext db, IStripeGateway stripe, IAuditLogService audit)
    {
        _db = db;
        _stripe = stripe;
        _audit = audit;
    }

    public List<Coupon> Coupons { get; set; } = new();

    [BindProperty]
    public CouponInput Input { get; set; } = new();

    public class CouponInput
    {
        [Required] [StringLength(50)] public string Code { get; set; } = string.Empty;
        [Range(1, 100)] public int DiscountPercent { get; set; } = 10;
        [Range(0, 100000)] public int MaxUses { get; set; }
        [DataType(DataType.Date)] public DateTime? ExpiresAt { get; set; }
    }

    public async Task OnGetAsync()
    {
        Coupons = await _db.Coupons.OrderByDescending(c => c.CreatedAt).ToListAsync();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (!ModelState.IsValid)
        {
            Coupons = await _db.Coupons.OrderByDescending(c => c.CreatedAt).ToListAsync();
            return Page();
        }

        var code = Input.Code.Trim().ToUpperInvariant();
        if (await _db.Coupons.AnyAsync(c => c.Code == code))
        {
            TempData["Error"] = "A coupon with that code already exists.";
            return RedirectToPage();
        }

        var coupon = new Coupon
        {
            Code = code,
            DiscountPercent = Input.DiscountPercent,
            MaxUses = Input.MaxUses,
            ExpiresAt = Input.ExpiresAt.HasValue ? DateTime.SpecifyKind(Input.ExpiresAt.Value, DateTimeKind.Utc) : null,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        coupon.StripeCouponId = await _stripe.SyncCouponAsync(coupon);
        _db.Coupons.Add(coupon);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "Coupon", coupon.Id.ToString(), coupon.Code);
        TempData["Success"] = $"Coupon '{coupon.Code}' created and synced with Stripe.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var c = await _db.Coupons.FindAsync(id);
        if (c == null) { TempData["Error"] = "Not found."; return RedirectToPage(); }
        c.IsActive = !c.IsActive;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Coupon '{c.Code}' {(c.IsActive ? "activated" : "deactivated")}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var c = await _db.Coupons.FindAsync(id);
        if (c == null) { TempData["Error"] = "Not found."; return RedirectToPage(); }
        _db.Coupons.Remove(c);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "Coupon", id.ToString(), c.Code);
        TempData["Success"] = $"Coupon '{c.Code}' deleted.";
        return RedirectToPage();
    }
}
