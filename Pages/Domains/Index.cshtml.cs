using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Pages.Domains;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _auditLog;
    private readonly INginxService _nginx;

    public IndexModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IAuditLogService auditLog,
        INginxService nginx)
    {
        _db = db;
        _userManager = userManager;
        _auditLog = auditLog;
        _nginx = nginx;
    }

    public List<Domain> Domains { get; set; } = new();
    public bool ShowOwner { get; set; }

    public async Task OnGetAsync()
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser == null) return;

        IQueryable<Domain> query = _db.Domains.Include(d => d.User);

        if (User.IsInRole(Roles.SuperAdmin))
        {
            ShowOwner = true;
        }
        else if (User.IsInRole(Roles.Reseller))
        {
            ShowOwner = true;
            query = query.Where(d => d.User != null && (d.UserId == currentUser.Id || d.User.ResellerId == currentUser.Id));
        }
        else
        {
            query = query.Where(d => d.UserId == currentUser.Id);
        }

        Domains = await query.OrderByDescending(d => d.CreatedAt).ToListAsync();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var domain = await _db.Domains.Include(d => d.User).FirstOrDefaultAsync(d => d.Id == id);
        if (domain == null)
        {
            TempData["Error"] = "Domain not found.";
            return RedirectToPage();
        }

        if (!await CanManageDomainAsync(domain))
        {
            TempData["Error"] = "You do not have permission to manage this domain.";
            return RedirectToPage();
        }

        var domainName = domain.DomainName;
        _db.Domains.Remove(domain);
        await _db.SaveChangesAsync();

        await _auditLog.LogAsync("Delete", "Domain", id.ToString(), domainName);

        var nginxResult = await _nginx.DeleteVirtualHostAsync(domainName);
        var suffix = nginxResult.Simulated ? " (nginx vhost removal simulated)" : " nginx vhost removed.";
        TempData["Success"] = $"Domain '{domainName}' has been removed.{suffix}";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(int id)
    {
        var domain = await _db.Domains.Include(d => d.User).FirstOrDefaultAsync(d => d.Id == id);
        if (domain == null)
        {
            TempData["Error"] = "Domain not found.";
            return RedirectToPage();
        }

        if (!await CanManageDomainAsync(domain))
        {
            TempData["Error"] = "You do not have permission to manage this domain.";
            return RedirectToPage();
        }

        domain.IsActive = !domain.IsActive;
        await _db.SaveChangesAsync();

        await _auditLog.LogAsync(domain.IsActive ? "Enable" : "Disable", "Domain", id.ToString(), domain.DomainName);
        TempData["Success"] = $"Domain '{domain.DomainName}' has been {(domain.IsActive ? "enabled" : "disabled")}.";
        return RedirectToPage();
    }

    private async Task<bool> CanManageDomainAsync(Domain domain)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser == null) return false;

        if (User.IsInRole(Roles.SuperAdmin)) return true;

        if (User.IsInRole(Roles.Reseller))
        {
            return domain.UserId == currentUser.Id || domain.User?.ResellerId == currentUser.Id;
        }

        return domain.UserId == currentUser.Id;
    }
}
