using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Store;

namespace SRXPanel.Pages.Client;

public class StoreAddonsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IStoreService _store;

    public StoreAddonsModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IStoreService store)
    {
        _db = db;
        _userManager = userManager;
        _store = store;
    }

    public List<Addon> Addons { get; private set; } = new();
    public List<ClientAddon> Purchased { get; private set; } = new();
    public bool HasCard { get; private set; }

    public async Task OnGetAsync()
    {
        Addons = (await _db.Addons.Where(a => a.IsActive).ToListAsync()).OrderBy(a => a.SortOrder).ToList();
        var user = await _userManager.GetUserAsync(User);
        if (user != null)
        {
            HasCard = await _store.GetDefaultCardAsync(user.Id) != null;
            Purchased = await _db.ClientAddons.Include(a => a.Addon)
                .Where(a => a.UserId == user.Id && a.IsActive)
                .OrderByDescending(a => a.PurchasedAt).ToListAsync();
        }
    }

    public async Task<IActionResult> OnPostBuyAsync(int addonId, int quantity = 1)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (await _store.GetDefaultCardAsync(user.Id) == null)
        {
            var addon = await _db.Addons.FindAsync(addonId);
            if (addon != null)
            {
                await _store.AddToCartAsync(user.Id, CartItemType.Addon, addon.Id, addon.Name, addon.Price, quantity: quantity);
                TempData["Success"] = $"Added {addon.Name} to your cart. Complete checkout to activate.";
                return RedirectToPage("/Client/Cart");
            }
        }

        var (ok, message) = await _store.PurchaseAddonAsync(user, addonId, quantity);
        TempData[ok ? "Success" : "Error"] = message;
        return RedirectToPage();
    }
}
