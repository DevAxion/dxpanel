using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Pages.Reseller;

public class InvoicePdfModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public InvoicePdfModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public ResellerInvoice Invoice { get; set; } = null!;
    public ResellerInvoiceSettings Settings { get; set; } = new();
    public ResellerPaymentSettings Payment { get; set; } = new();
    public ResellerBranding? Branding { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var invoice = await _db.ResellerInvoices.FirstOrDefaultAsync(i => i.Id == id);
        // A reseller sees own invoices; a SuperAdmin may see any.
        if (invoice == null || (invoice.ResellerId != user.Id && !User.IsInRole(Roles.SuperAdmin)))
            return NotFound();

        Invoice = invoice;
        Settings = await _db.ResellerInvoiceSettings.FirstOrDefaultAsync(s => s.ResellerId == invoice.ResellerId)
                   ?? new ResellerInvoiceSettings { ResellerId = invoice.ResellerId };
        Payment = await _db.ResellerPaymentSettings.FirstOrDefaultAsync(s => s.ResellerId == invoice.ResellerId)
                  ?? new ResellerPaymentSettings { ResellerId = invoice.ResellerId };
        Branding = await _db.ResellerBrandings.FirstOrDefaultAsync(b => b.ResellerId == invoice.ResellerId);

        TaxAmount = Math.Round(invoice.Amount * Payment.TaxRatePercent / 100m, 2);
        Total = invoice.Amount + TaxAmount;
        return Page();
    }
}
