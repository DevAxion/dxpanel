using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Store;

namespace SRXPanel.Pages.Client;

public class StoreModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IStoreService _store;

    public StoreModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IStoreService store)
    {
        _db = db;
        _userManager = userManager;
        _store = store;
    }

    public List<Plan> Plans { get; private set; } = new();
    public List<VpsPlan> VpsPlans { get; private set; } = new();
    public List<ResellerPackage> ResellerPackages { get; private set; } = new();
    public bool HasCard { get; private set; }

    public async Task OnGetAsync()
    {
        Plans = (await _db.Plans.Where(p => p.IsActive && p.BillingCycle == BillingCycle.Monthly).ToListAsync())
            .OrderBy(p => p.Price).ToList();
        VpsPlans = (await _db.VpsPlans.Where(v => v.IsActive).ToListAsync())
            .OrderBy(v => v.SortOrder).ThenBy(v => v.Price).ToList();
        ResellerPackages = (await _db.ResellerPackages.Where(r => r.IsPublic && r.IsActive).ToListAsync())
            .OrderBy(r => r.Price).ToList();

        var user = await _userManager.GetUserAsync(User);
        if (user != null) HasCard = await _store.GetDefaultCardAsync(user.Id) != null;
    }

    // Shared hosting: immediate order if a card is saved, else go to the checkout page.
    public async Task<IActionResult> OnPostOrderAsync(int planId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var card = await _store.GetDefaultCardAsync(user.Id);
        if (card == null)
        {
            return RedirectToPage("/Checkout/Index", new { planId });
        }

        var (ok, message, subId) = await _store.OrderPlanAsync(user, planId);
        TempData[ok ? "Success" : "Error"] = message;
        return ok ? RedirectToPage("/Client/Services") : RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddToCartAsync(CartItemType type, int referenceId, string label, decimal price, int quantity = 1)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        await _store.AddToCartAsync(user.Id, type, referenceId, label, price, quantity: quantity);
        TempData["Success"] = $"Added \"{label}\" to your cart.";
        return RedirectToPage("/Client/Cart");
    }
}
