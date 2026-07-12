using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Pages.Email;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserScopeService _scope;
    private readonly IAuditLogService _audit;
    private readonly IEmailService _email;

    public IndexModel(ApplicationDbContext db, IUserScopeService scope, IAuditLogService audit, IEmailService email)
    {
        _db = db;
        _scope = scope;
        _audit = audit;
        _email = email;
    }

    public List<EmailAccount> Accounts { get; set; } = new();
    public List<EmailForwarder> Forwarders { get; set; } = new();
    public bool ShowOwner { get; set; }

    public async Task OnGetAsync()
    {
        ShowOwner = User.IsInRole(Roles.SuperAdmin) || User.IsInRole(Roles.Reseller);
        var manageable = await _scope.GetManageableUserIdsAsync(User);

        Accounts = await _db.EmailAccounts.Include(e => e.User).Include(e => e.Domain)
            .Where(e => manageable.Contains(e.UserId))
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();

        Forwarders = await _db.EmailForwarders.Include(e => e.User)
            .Where(e => manageable.Contains(e.UserId))
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(int id)
    {
        var account = await _db.EmailAccounts.FindAsync(id);
        if (account == null || !await _scope.CanManageUserAsync(User, account.UserId))
        {
            TempData["Error"] = "Email account not found or access denied.";
            return RedirectToPage();
        }

        account.IsActive = !account.IsActive;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(account.IsActive ? "Enable" : "Disable", "EmailAccount", id.ToString(), account.EmailAddress);
        TempData["Success"] = $"Email account '{account.EmailAddress}' has been {(account.IsActive ? "enabled" : "disabled")}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var account = await _db.EmailAccounts.FindAsync(id);
        if (account == null || !await _scope.CanManageUserAsync(User, account.UserId))
        {
            TempData["Error"] = "Email account not found or access denied.";
            return RedirectToPage();
        }

        var name = account.EmailAddress;
        _db.EmailAccounts.Remove(account);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "EmailAccount", id.ToString(), name);

        var mailResult = await _email.DeleteMailboxAsync(name);
        var suffix = mailResult.Simulated ? " (mailbox removal simulated)" : " mailbox removed.";
        TempData["Success"] = $"Email account '{name}' has been deleted.{suffix}";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteForwarderAsync(int id)
    {
        var fwd = await _db.EmailForwarders.FindAsync(id);
        if (fwd == null || !await _scope.CanManageUserAsync(User, fwd.UserId))
        {
            TempData["Error"] = "Forwarder not found or access denied.";
            return RedirectToPage();
        }

        var source = fwd.Source;
        _db.EmailForwarders.Remove(fwd);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "EmailForwarder", id.ToString(), $"{fwd.Source} -> {fwd.Destination}");

        await _email.DeleteForwarderAsync(source);
        TempData["Success"] = "Forwarder has been deleted.";
        return RedirectToPage();
    }
}
