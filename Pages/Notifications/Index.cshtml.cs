using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;

namespace SRXPanel.Pages.Notifications;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly INotificationService _notifications;

    public IndexModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, INotificationService notifications)
    {
        _db = db;
        _userManager = userManager;
        _notifications = notifications;
    }

    public List<Notification> Items { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        Items = await _db.Notifications
            .Where(n => n.UserId == user.Id)
            .OrderByDescending(n => n.CreatedAt)
            .Take(100)
            .ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostMarkReadAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        await _notifications.MarkAsReadAsync(user.Id, id);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostMarkAllReadAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        await _notifications.MarkAllAsReadAsync(user.Id);
        TempData["Success"] = "All notifications marked as read.";
        return RedirectToPage();
    }
}
