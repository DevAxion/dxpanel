using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Pages.Reseller;

public class PaymentSettingsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICommandRunner _log;
    private readonly IPlatformSettingsService _platform;

    public PaymentSettingsModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        ICommandRunner log, IPlatformSettingsService platform)
    {
        _db = db;
        _userManager = userManager;
        _log = log;
        _platform = platform;
    }

    public ResellerPaymentSettings Settings { get; set; } = new();
    public decimal PlatformFee { get; set; }

    private async Task<(ApplicationUser?, ResellerPaymentSettings)> LoadAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return (null, new ResellerPaymentSettings());
        var settings = await _db.ResellerPaymentSettings.FirstOrDefaultAsync(s => s.ResellerId == user.Id);
        if (settings == null)
        {
            settings = new ResellerPaymentSettings { ResellerId = user.Id };
            _db.ResellerPaymentSettings.Add(settings);
            await _db.SaveChangesAsync();
        }
        Settings = settings;
        PlatformFee = (await _platform.GetAsync()).PlatformFeePercent;
        return (user, settings);
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var (user, _) = await LoadAsync();
        if (user == null) return Challenge();
        return Page();
    }

    public async Task<IActionResult> OnPostConnectAsync()
    {
        var (user, settings) = await LoadAsync();
        if (user == null) return Challenge();

        // Simulation-safe Stripe Connect Express onboarding.
        settings.StripeConnectAccountId = $"acct_{Guid.NewGuid():N}"[..18];
        settings.ConnectOnboarded = true;
        settings.UseOwnKeys = false;
        settings.UpdatedAt = DateTime.UtcNow;
        await _log.LogExternalAsync($"stripe.accounts.create(type=express, reseller={user.Id})",
            $"connected {settings.StripeConnectAccountId}", true, "stripe");
        await _db.SaveChangesAsync();
        TempData["Success"] = "Stripe account connected (Express).";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDisconnectAsync()
    {
        var (user, settings) = await LoadAsync();
        if (user == null) return Challenge();
        settings.ConnectOnboarded = false;
        settings.StripeConnectAccountId = null;
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Stripe account disconnected.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveAsync(bool useOwnKeys, string? ownPublishableKey, string? ownSecretKey,
        string acceptedMethods, string currency, decimal taxRatePercent, string taxLabel, string? taxNumber, bool showTaxNumber)
    {
        var (user, settings) = await LoadAsync();
        if (user == null) return Challenge();

        settings.UseOwnKeys = useOwnKeys;
        settings.OwnPublishableKey = ownPublishableKey;
        settings.OwnSecretKey = ownSecretKey;
        settings.AcceptedMethods = string.IsNullOrWhiteSpace(acceptedMethods) ? "card" : acceptedMethods.Trim();
        settings.Currency = currency;
        settings.TaxRatePercent = taxRatePercent;
        settings.TaxLabel = string.IsNullOrWhiteSpace(taxLabel) ? "VAT" : taxLabel.Trim();
        settings.TaxNumber = taxNumber;
        settings.ShowTaxNumberOnInvoice = showTaxNumber;
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Payment settings saved.";
        return RedirectToPage();
    }
}
