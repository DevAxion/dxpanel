using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Store;

namespace SRXPanel.Pages.Client;

public class InvoicesModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IStoreService _store;

    public InvoicesModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IStoreService store)
    {
        _db = db;
        _userManager = userManager;
        _store = store;
    }

    public List<Invoice> Invoices { get; private set; } = new();

    public bool IsOverdue(Invoice i) => i.Status == InvoiceStatus.Unpaid && i.DueDate.Date < DateTime.UtcNow.Date;

    public async Task OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return;
        Invoices = await _db.Invoices.Where(i => i.UserId == user.Id)
            .OrderByDescending(i => i.CreatedAt).ToListAsync();
    }

    public async Task<IActionResult> OnPostPayAsync(int invoiceId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var (ok, message) = await _store.PayInvoiceAsync(user, invoiceId);
        TempData[ok ? "Success" : "Error"] = message;
        return RedirectToPage();
    }
}
