using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Store;

namespace SRXPanel.Pages.Users;

public class UserRow
{
    public string Id { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? PackageName { get; set; }
    public int DomainCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IAuditLogService _auditLog;
    private readonly INotificationService _notifications;
    private readonly ISmsSender _sms;

    public IndexModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager, IAuditLogService auditLog,
        INotificationService notifications, ISmsSender sms)
    {
        _db = db;
        _userManager = userManager;
        _signInManager = signInManager;
        _auditLog = auditLog;
        _notifications = notifications;
        _sms = sms;
    }

    public List<UserRow> Users { get; set; } = new();

    public async Task OnGetAsync()
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser == null) return;

        IQueryable<ApplicationUser> query = _db.Users.Include(u => u.Package).Include(u => u.Domains);

        if (User.IsInRole(Roles.Reseller))
        {
            query = query.Where(u => u.ResellerId == currentUser.Id);
        }
        else
        {
            // SuperAdmin sees everyone except themselves in the manageable list
            query = query.Where(u => u.Id != currentUser.Id);
        }

        var users = await query.ToListAsync();

        foreach (var u in users)
        {
            var roles = await _userManager.GetRolesAsync(u);
            Users.Add(new UserRow
            {
                Id = u.Id,
                UserName = u.UserName ?? string.Empty,
                Email = u.Email ?? string.Empty,
                FullName = u.FullName,
                Role = roles.FirstOrDefault() ?? "-",
                IsActive = u.IsActive,
                PackageName = u.Package?.Name,
                DomainCount = u.Domains.Count,
                CreatedAt = u.CreatedAt
            });
        }

        Users = Users.OrderByDescending(u => u.CreatedAt).ToList();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            TempData["Error"] = "User not found.";
            return RedirectToPage();
        }

        if (!await CanManageUserAsync(user))
        {
            TempData["Error"] = "You do not have permission to manage this user.";
            return RedirectToPage();
        }

        user.IsActive = !user.IsActive;
        await _userManager.UpdateAsync(user);

        if (!user.IsActive)
        {
            await _notifications.NotifyAsync(user.Id, "Account suspended",
                "Your hosting account has been suspended. Please contact support.", NotificationType.Error);
            // SMS alert when the customer has a phone number on file.
            await _sms.SendAsync(user.PhoneNumber,
                $"SRXPanel: your hosting account '{user.UserName}' has been suspended. Please contact support.");
        }
        else
        {
            await _notifications.NotifyAsync(user.Id, "Account reactivated",
                "Your hosting account has been reactivated.", NotificationType.Success);
            await _sms.SendAsync(user.PhoneNumber,
                $"SRXPanel: your hosting account '{user.UserName}' has been reactivated.");
        }

        await _auditLog.LogAsync(user.IsActive ? "Unsuspend" : "Suspend", "User", user.Id, user.UserName);
        TempData["Success"] = $"User '{user.UserName}' has been {(user.IsActive ? "unsuspended" : "suspended")}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostImpersonateAsync(string id)
    {
        // Impersonation is SuperAdmin-only regardless of folder policy.
        if (!User.IsInRole(Roles.SuperAdmin))
        {
            return Forbid();
        }

        var target = await _userManager.FindByIdAsync(id);
        var admin = await _userManager.GetUserAsync(User);
        if (target == null || admin == null)
        {
            TempData["Error"] = "User not found.";
            return RedirectToPage();
        }

        if (target.Id == admin.Id)
        {
            TempData["Error"] = "You cannot impersonate yourself.";
            return RedirectToPage();
        }

        var extraClaims = new List<System.Security.Claims.Claim>
        {
            new("OriginalAdminId", admin.Id),
            new("OriginalAdminName", admin.UserName ?? "admin")
        };

        await _signInManager.SignInWithClaimsAsync(target, isPersistent: false, extraClaims);
        await _auditLog.LogAsync("Impersonate", "User", target.Id, $"{admin.UserName} impersonated {target.UserName}");

        return RedirectToPage("/Dashboard/Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            TempData["Error"] = "User not found.";
            return RedirectToPage();
        }

        if (!await CanManageUserAsync(user))
        {
            TempData["Error"] = "You do not have permission to manage this user.";
            return RedirectToPage();
        }

        var userName = user.UserName;
        var result = await _userManager.DeleteAsync(user);
        if (result.Succeeded)
        {
            await _auditLog.LogAsync("Delete", "User", id, userName);
            TempData["Success"] = $"User '{userName}' has been deleted.";
        }
        else
        {
            TempData["Error"] = "Failed to delete user.";
        }

        return RedirectToPage();
    }

    private async Task<bool> CanManageUserAsync(ApplicationUser target)
    {
        if (User.IsInRole(Roles.SuperAdmin))
        {
            return true;
        }

        if (User.IsInRole(Roles.Reseller))
        {
            var currentUser = await _userManager.GetUserAsync(User);
            return currentUser != null && target.ResellerId == currentUser.Id;
        }

        return false;
    }
}
