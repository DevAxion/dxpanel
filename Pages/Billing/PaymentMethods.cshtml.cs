using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Billing;

namespace SRXPanel.Pages.Billing;

public class PaymentMethodsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IBillingService _billing;
    private readonly IStripeGateway _stripe;

    public PaymentMethodsModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        IBillingService billing, IStripeGateway stripe)
    {
        _db = db;
        _userManager = userManager;
        _billing = billing;
        _stripe = stripe;
    }

    public List<PaymentMethod> Methods { get; set; } = new();
    public bool SimulationMode => _stripe.SimulationMode;
    public string PublishableKey => _stripe.PublishableKey;

    [BindProperty]
    public string? PaymentMethodToken { get; set; }

    private async Task LoadAsync(ApplicationUser user)
    {
        Methods = await _db.PaymentMethods.Where(p => p.UserId == user.Id)
            .OrderByDescending(p => p.IsDefault).ThenByDescending(p => p.CreatedAt).ToListAsync();
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        await LoadAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAddAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        await _billing.AddPaymentMethodAsync(user, PaymentMethodToken);
        TempData["Success"] = "Card added.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDefaultAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        await _billing.SetDefaultPaymentMethodAsync(user, id);
        TempData["Success"] = "Default card updated.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        await _billing.RemovePaymentMethodAsync(user, id);
        TempData["Success"] = "Card removed.";
        return RedirectToPage();
    }
}
