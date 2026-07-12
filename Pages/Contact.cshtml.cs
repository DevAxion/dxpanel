using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;

namespace SRXPanel.Pages;

[AllowAnonymous]
public class ContactModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly INotificationService _notifications;

    public ContactModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, INotificationService notifications)
    {
        _db = db;
        _userManager = userManager;
        _notifications = notifications;
    }

    [BindProperty] public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required, StringLength(120)] public string Name { get; set; } = string.Empty;
        [Required, EmailAddress, StringLength(200)] public string Email { get; set; } = string.Empty;
        [StringLength(200)] public string? Subject { get; set; }
        [Required, StringLength(4000)] public string Message { get; set; } = string.Empty;
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var msg = new ContactMessage
        {
            Name = Input.Name,
            Email = Input.Email,
            Subject = Input.Subject,
            Message = Input.Message,
            CreatedAt = DateTime.UtcNow
        };
        _db.ContactMessages.Add(msg);
        await _db.SaveChangesAsync();

        // Notify all SuperAdmins of the new enquiry.
        var admins = await _userManager.GetUsersInRoleAsync(Roles.SuperAdmin);
        foreach (var admin in admins)
        {
            await _notifications.NotifyAsync(admin.Id, "New contact message",
                $"{Input.Name}: {Input.Subject ?? "(no subject)"}", NotificationType.Info, dedupeKey: $"contact-{msg.Id}");
        }

        TempData["ContactSuccess"] = "Thanks for reaching out! We'll get back to you shortly.";
        return RedirectToPage();
    }
}
