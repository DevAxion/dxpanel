using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Pages.Client;

public class InvoicePdfModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public InvoicePdfModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public Invoice Invoice { get; private set; } = null!;
    public ApplicationUser Client { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var invoice = await _db.Invoices.FindAsync(id);
        if (invoice == null || invoice.UserId != user.Id) return NotFound();

        Invoice = invoice;
        Client = user;
        return Page();
    }
}
