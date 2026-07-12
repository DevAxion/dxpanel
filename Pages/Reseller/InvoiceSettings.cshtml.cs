using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Pages.Reseller;

public class InvoiceSettingsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _env;

    public InvoiceSettingsModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IWebHostEnvironment env)
    {
        _db = db;
        _userManager = userManager;
        _env = env;
    }

    public ResellerInvoiceSettings Settings { get; set; } = new();
    [BindProperty] public IFormFile? Logo { get; set; }

    private async Task<(ApplicationUser?, ResellerInvoiceSettings)> LoadAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return (null, new ResellerInvoiceSettings());
        var settings = await _db.ResellerInvoiceSettings.FirstOrDefaultAsync(s => s.ResellerId == user.Id);
        if (settings == null)
        {
            settings = new ResellerInvoiceSettings { ResellerId = user.Id };
            _db.ResellerInvoiceSettings.Add(settings);
            await _db.SaveChangesAsync();
        }
        Settings = settings;
        return (user, settings);
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var (user, _) = await LoadAsync();
        if (user == null) return Challenge();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? companyName, string? companyAddress, string? taxNumber,
        string invoicePrefix, string? paymentTerms, string? footerNotes, string? bankDetails)
    {
        var (user, settings) = await LoadAsync();
        if (user == null) return Challenge();

        settings.CompanyName = companyName;
        settings.CompanyAddress = companyAddress;
        settings.TaxNumber = taxNumber;
        settings.InvoicePrefix = string.IsNullOrWhiteSpace(invoicePrefix) ? "INV-" : invoicePrefix.Trim();
        settings.PaymentTerms = paymentTerms;
        settings.FooterNotes = footerNotes;
        settings.BankDetails = bankDetails;

        if (Logo is { Length: > 0 } and { Length: <= 2 * 1024 * 1024 })
        {
            var ext = Path.GetExtension(Logo.FileName).ToLowerInvariant();
            if (ext is ".png" or ".jpg" or ".jpeg" or ".svg")
            {
                var dir = Path.Combine(_env.WebRootPath, "branding", user.Id);
                Directory.CreateDirectory(dir);
                var full = Path.Combine(dir, $"invoice-logo{ext}");
                await using var stream = System.IO.File.Create(full);
                await Logo.CopyToAsync(stream);
                settings.LogoPath = $"/branding/{user.Id}/invoice-logo{ext}";
            }
        }

        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Invoice settings saved.";
        return RedirectToPage();
    }
}
