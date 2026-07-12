using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services.Billing;

namespace SRXPanel.Pages.Reseller;

public class BillingModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IResellerBillingService _billing;

    public BillingModel(UserManager<ApplicationUser> userManager, IResellerBillingService billing)
    {
        _userManager = userManager;
        _billing = billing;
    }

    public ResellerBillingConfig Config { get; set; } = new();
    public decimal Balance { get; set; }
    public List<ResellerTransaction> Transactions { get; set; } = new();
    public List<ResellerInvoice> Invoices { get; set; } = new();
    public ResellerTransactionType? Filter { get; set; }

    private async Task<ApplicationUser?> LoadAsync(ResellerTransactionType? filter)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return null;
        Config = await _billing.GetConfigAsync(user.Id);
        Balance = await _billing.GetBalanceAsync(user.Id);
        Filter = filter;
        Transactions = await _billing.GetTransactionsAsync(user.Id, filter);
        Invoices = await _billing.GetInvoicesAsync(user.Id);
        return user;
    }

    public async Task<IActionResult> OnGetAsync(ResellerTransactionType? filter = null)
    {
        if (await LoadAsync(filter) == null) return Challenge();
        return Page();
    }

    public async Task<IActionResult> OnPostAddCreditAsync(decimal amount)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        if (amount <= 0) { TempData["Error"] = "Enter a positive amount."; return RedirectToPage(); }
        await _billing.AddCreditViaStripeAsync(user.Id, amount);
        TempData["Success"] = $"Added {amount:0.00} credit to your balance.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAutoTopUpAsync(bool enabled, decimal threshold, decimal topUpAmount)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var config = await _billing.GetConfigAsync(user.Id);
        config.AutoTopUpEnabled = enabled;
        config.AutoTopUpThreshold = threshold;
        config.AutoTopUpAmount = topUpAmount;
        await _billing.SaveConfigAsync(config);
        TempData["Success"] = "Auto top-up settings saved.";
        return RedirectToPage();
    }
}
