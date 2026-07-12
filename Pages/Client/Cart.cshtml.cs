using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services.Billing;
using SRXPanel.Services.Store;

namespace SRXPanel.Pages.Client;

public class CartModel : PageModel
{
    private readonly IStoreService _store;
    private readonly IBillingService _billing;
    private readonly UserManager<ApplicationUser> _userManager;

    public CartModel(IStoreService store, IBillingService billing, UserManager<ApplicationUser> userManager)
    {
        _store = store;
        _billing = billing;
        _userManager = userManager;
    }

    public List<CartItem> Items { get; private set; } = new();
    public decimal Subtotal { get; private set; }
    public decimal Tax { get; private set; }
    public decimal Total { get; private set; }
    public Coupon? AppliedCoupon { get; private set; }
    public bool HasCard { get; private set; }

    [BindProperty] public string? CouponCode { get; set; }

    private const decimal TaxRate = 0.00m; // configurable tax rate

    private async Task LoadAsync(string userId)
    {
        Items = await _store.GetCartAsync(userId);
        Subtotal = Items.Sum(i => i.LineTotal);
        AppliedCoupon = _billing.ValidateCoupon(CouponCode);
        var discounted = _billing.ApplyDiscount(Subtotal, AppliedCoupon);
        Tax = Math.Round(discounted * TaxRate, 2);
        Total = discounted + Tax;
        HasCard = await _store.GetDefaultCardAsync(userId) != null;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        await LoadAsync(user.Id);
        return Page();
    }

    public async Task<IActionResult> OnPostApplyCouponAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        await LoadAsync(user.Id);
        TempData[AppliedCoupon != null ? "Success" : "Error"] =
            AppliedCoupon != null ? $"Coupon applied — {AppliedCoupon.DiscountPercent}% off." : "Invalid or expired coupon.";
        return Page();
    }

    public async Task<IActionResult> OnPostRemoveAsync(int cartItemId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        await _store.RemoveFromCartAsync(user.Id, cartItemId);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCheckoutAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var (ok, message) = await _store.CheckoutCartAsync(user, CouponCode);
        TempData[ok ? "Success" : "Error"] = message;
        return ok ? RedirectToPage("/Client/Services") : RedirectToPage();
    }
}
