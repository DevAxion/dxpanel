using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Pages.Admin;

public class MessagesModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public MessagesModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public List<ContactMessage> Messages { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Messages = await _db.ContactMessages.OrderByDescending(m => m.CreatedAt).ToListAsync();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var m = await _db.ContactMessages.FindAsync(id);
        if (m != null) { m.IsRead = !m.IsRead; await _db.SaveChangesAsync(); }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var m = await _db.ContactMessages.FindAsync(id);
        if (m != null) { _db.ContactMessages.Remove(m); await _db.SaveChangesAsync(); }
        return RedirectToPage();
    }
}
