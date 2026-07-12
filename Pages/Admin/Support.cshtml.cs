using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Portal;

namespace SRXPanel.Pages.Admin;

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
    public List<CannedResponse> Canned { get; set; } = new();
    public List<SelectListItem> Staff { get; set; } = new();

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty(SupportsGet = true)] public string? StatusFilter { get; set; }
    [BindProperty(SupportsGet = true)] public string? PriorityFilter { get; set; }

    private async Task LoadAsync()
    {
        var query = _db.Tickets.Include(t => t.User).AsQueryable();
        if (Enum.TryParse<TicketStatus>(StatusFilter, out var st)) query = query.Where(t => t.Status == st);
        if (Enum.TryParse<TicketPriority>(PriorityFilter, out var pr)) query = query.Where(t => t.Priority == pr);
        Tickets = await query.OrderByDescending(t => t.UpdatedAt).ToListAsync();

        Canned = await _db.CannedResponses.OrderBy(c => c.Title).ToListAsync();
        var admins = await _userManager.GetUsersInRoleAsync(Roles.SuperAdmin);
        Staff = admins.Select(a => new SelectListItem(a.UserName, a.Id)).ToList();

        if (Id is int id)
        {
            Selected = await _db.Tickets.Include(t => t.User).FirstOrDefaultAsync(t => t.Id == id);
            if (Selected != null)
                Replies = await _db.TicketReplies.Include(r => r.User).Where(r => r.TicketId == id).OrderBy(r => r.CreatedAt).ToListAsync();
        }
    }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostReplyAsync(int ticketId, string message)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        if (!string.IsNullOrWhiteSpace(message))
            await _tickets.ReplyAsync(ticketId, user.Id, message.Trim(), isStaff: true, null);
        return RedirectToPage(new { id = ticketId });
    }

    public async Task<IActionResult> OnPostStatusAsync(int ticketId, TicketStatus status)
    {
        await _tickets.SetStatusAsync(ticketId, status);
        return RedirectToPage(new { id = ticketId });
    }

    public async Task<IActionResult> OnPostPriorityAsync(int ticketId, TicketPriority priority)
    {
        await _tickets.SetPriorityAsync(ticketId, priority);
        return RedirectToPage(new { id = ticketId });
    }

    public async Task<IActionResult> OnPostAssignAsync(int ticketId, string? staffId)
    {
        await _tickets.AssignAsync(ticketId, string.IsNullOrEmpty(staffId) ? null : staffId);
        return RedirectToPage(new { id = ticketId });
    }

    public async Task<IActionResult> OnPostAddCannedAsync(string title, string body)
    {
        if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(body))
        {
            _db.CannedResponses.Add(new CannedResponse { Title = title.Trim(), Body = body.Trim(), CreatedAt = DateTime.UtcNow });
            await _db.SaveChangesAsync();
            TempData["Success"] = "Canned response saved.";
        }
        return RedirectToPage(new { id = Id });
    }
}
