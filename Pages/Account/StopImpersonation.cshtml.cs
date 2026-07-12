using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;

namespace SRXPanel.Pages.Account;

public class StopImpersonationModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IAuditLogService _auditLog;
    private readonly ApplicationDbContext _db;

    public StopImpersonationModel(UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager, IAuditLogService auditLog, ApplicationDbContext db)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _auditLog = auditLog;
        _db = db;
    }

    public IActionResult OnGet() => RedirectToPage("/Dashboard/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        var originalAdminId = User.FindFirst("OriginalAdminId")?.Value;
        if (string.IsNullOrEmpty(originalAdminId))
        {
            return RedirectToPage("/Dashboard/Index");
        }

        var admin = await _userManager.FindByIdAsync(originalAdminId);
        if (admin == null)
        {
            await _signInManager.SignOutAsync();
            return RedirectToPage("/Account/Login");
        }

        var impersonatedName = User.Identity?.Name;

        // Close any active impersonation session records for this admin/reseller.
        var openSessions = await _db.ImpersonationSessions
            .Where(s => s.ImpersonatorId == originalAdminId && s.IsActive)
            .ToListAsync();
        foreach (var s in openSessions)
        {
            s.IsActive = false;
            s.EndedAt = DateTime.UtcNow;
        }
        if (openSessions.Count > 0) await _db.SaveChangesAsync();

        await _signInManager.SignInAsync(admin, isPersistent: false);
        await _auditLog.LogAsync("StopImpersonation", "User", admin.Id, $"{admin.UserName} stopped impersonating {impersonatedName}");

        TempData["Success"] = "Returned to your account.";
        return RedirectToPage("/Dashboard/Index");
    }
}
