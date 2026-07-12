using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services.Store;

namespace SRXPanel.Pages.Client;

public class DomainRegisterModel : PageModel
{
    private readonly IStoreService _store;
    private readonly UserManager<ApplicationUser> _userManager;

    public DomainRegisterModel(IStoreService store, UserManager<ApplicationUser> userManager)
    {
        _store = store;
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public record Result(string Domain, decimal Price, bool Available);
    public List<Result> Results { get; private set; } = new();

    // Offered TLDs and their annual price (registrar integration mocked).
    private static readonly (string Tld, decimal Price)[] Tlds =
    {
        (".com", 12.99m), (".net", 14.99m), (".org", 13.49m),
        (".az", 29.99m), (".io", 39.99m), (".dev", 15.99m)
    };

    public void OnGet()
    {
        if (string.IsNullOrWhiteSpace(Q)) return;
        var sld = Q.Trim().ToLowerInvariant();
        var dot = sld.IndexOf('.');
        if (dot > 0) sld = sld[..dot];
        sld = new string(sld.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        if (string.IsNullOrEmpty(sld)) return;

        foreach (var (tld, price) in Tlds)
        {
            var available = Math.Abs(($"{sld}{tld}").GetHashCode()) % 3 != 0;
            Results.Add(new Result(sld + tld, price, available));
        }
    }

    public async Task<IActionResult> OnPostRegisterAsync(string domain, decimal price)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (await _store.GetDefaultCardAsync(user.Id) == null)
        {
            await _store.AddToCartAsync(user.Id, CartItemType.Domain, 0, $"Domain: {domain}", price, domainName: domain);
            TempData["Success"] = $"Added {domain} to your cart.";
            return RedirectToPage("/Client/Cart");
        }

        var (ok, message) = await _store.RegisterDomainAsync(user, domain, price);
        TempData[ok ? "Success" : "Error"] = message;
        return RedirectToPage(new { q = Q });
    }

    public async Task<IActionResult> OnPostAddToCartAsync(string domain, decimal price)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        await _store.AddToCartAsync(user.Id, CartItemType.Domain, 0, $"Domain: {domain}", price, domainName: domain);
        TempData["Success"] = $"Added {domain} to your cart.";
        return RedirectToPage("/Client/Cart");
    }
}
