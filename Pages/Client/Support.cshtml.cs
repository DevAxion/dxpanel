using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Portal;

namespace SRXPanel.Pages.Client;

public class SupportModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITicketService _tickets;

    public SupportModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, ITicketService tickets)
    {
        _db = db;
        _userManager = userManager;
        _tickets = tickets;
    }

    public List<Ticket> Tickets { get; set; } = new();
    public Ticket? Selected { get; set; }
    public List<TicketReply> Replies { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int? Id { get; set; }

    private async Task<ApplicationUser?> LoadAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return null;
        Tickets = await _db.Tickets.Where(t => t.UserId == user.Id)
            .OrderByDescending(t => t.UpdatedAt).ToListAsync();
        if (Id is int id)
        {
            Selected = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id && t.UserId == user.Id);
            if (Selected != null)
            {
                Replies = await _db.TicketReplies.Include(r => r.User)
                    .Where(r => r.TicketId == id).OrderBy(r => r.CreatedAt).ToListAsync();
            }
        }
        return user;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (await LoadAsync() == null) return Challenge();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(string subject, TicketPriority priority, string message)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(message))
        {
            TempData["Error"] = "Subject and message are required.";
            return RedirectToPage();
        }
        var ticket = await _tickets.CreateAsync(user.Id, subject.Trim(), priority, message.Trim(), null);
        TempData["Success"] = $"Ticket #{ticket.Id} created.";
        return RedirectToPage(new { id = ticket.Id });
    }

    public async Task<IActionResult> OnPostReplyAsync(int ticketId, string message)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId && t.UserId == user.Id);
        if (ticket == null) { TempData["Error"] = "Ticket not found."; return RedirectToPage(); }
        if (!string.IsNullOrWhiteSpace(message))
            await _tickets.ReplyAsync(ticketId, user.Id, message.Trim(), isStaff: false, null);
        return RedirectToPage(new { id = ticketId });
    }

    public async Task<IActionResult> OnPostCloseAsync(int ticketId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId && t.UserId == user.Id);
        if (ticket == null) { TempData["Error"] = "Not found."; return RedirectToPage(); }
        await _tickets.SetStatusAsync(ticketId, TicketStatus.Closed);
        TempData["Success"] = $"Ticket #{ticketId} closed.";
        return RedirectToPage(new { id = ticketId });
    }
}
