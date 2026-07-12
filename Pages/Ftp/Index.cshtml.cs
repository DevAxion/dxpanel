using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Pages.Ftp;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserScopeService _scope;
    private readonly IAuditLogService _audit;
    private readonly IFtpService _ftp;

    public IndexModel(ApplicationDbContext db, IUserScopeService scope, IAuditLogService audit, IFtpService ftp)
    {
        _db = db;
        _scope = scope;
        _audit = audit;
        _ftp = ftp;
    }

    public List<FtpAccount> Accounts { get; set; } = new();
    public bool ShowOwner { get; set; }

    public async Task OnGetAsync()
    {
        ShowOwner = User.IsInRole(Roles.SuperAdmin) || User.IsInRole(Roles.Reseller);
        var manageable = await _scope.GetManageableUserIdsAsync(User);
        Accounts = await _db.FtpAccounts.Include(f => f.User).Include(f => f.Domain)
            .Where(f => manageable.Contains(f.UserId))
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(int id)
    {
        var account = await _db.FtpAccounts.FindAsync(id);
        if (account == null || !await _scope.CanManageUserAsync(User, account.UserId))
        {
            TempData["Error"] = "FTP account not found or access denied.";
            return RedirectToPage();
        }

        account.IsActive = !account.IsActive;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(account.IsActive ? "Enable" : "Disable", "FtpAccount", id.ToString(), account.Username);
        TempData["Success"] = $"FTP account '{account.Username}' has been {(account.IsActive ? "enabled" : "disabled")}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var account = await _db.FtpAccounts.FindAsync(id);
        if (account == null || !await _scope.CanManageUserAsync(User, account.UserId))
        {
            TempData["Error"] = "FTP account not found or access denied.";
            return RedirectToPage();
        }

        var name = account.Username;
        _db.FtpAccounts.Remove(account);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "FtpAccount", id.ToString(), name);

        var ftpResult = await _ftp.DeleteFtpUserAsync(name);
        var suffix = ftpResult.Simulated ? " (vsftpd removal simulated)" : " vsftpd user removed.";
        TempData["Success"] = $"FTP account '{name}' has been deleted.{suffix}";
        return RedirectToPage();
    }
}
