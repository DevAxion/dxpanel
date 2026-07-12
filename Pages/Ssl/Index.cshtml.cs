using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Pages.Ssl;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserScopeService _scope;
    private readonly IAuditLogService _audit;
    private readonly ISslService _ssl;
    private readonly INginxService _nginx;

    public IndexModel(ApplicationDbContext db, IUserScopeService scope, IAuditLogService audit,
        ISslService ssl, INginxService nginx)
    {
        _db = db;
        _scope = scope;
        _audit = audit;
        _ssl = ssl;
        _nginx = nginx;
    }

    public List<SslCertificate> Certificates { get; set; } = new();
    public bool ShowOwner { get; set; }

    public async Task OnGetAsync()
    {
        ShowOwner = User.IsInRole(Roles.SuperAdmin) || User.IsInRole(Roles.Reseller);
        var manageable = await _scope.GetManageableUserIdsAsync(User);
        Certificates = await _db.SslCertificates.Include(c => c.Domain).Include(c => c.User)
            .Where(c => manageable.Contains(c.UserId))
            .OrderBy(c => c.ExpiresAt)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var cert = await _db.SslCertificates.FindAsync(id);
        if (cert == null || !await _scope.CanManageUserAsync(User, cert.UserId))
        {
            TempData["Error"] = "Certificate not found or access denied.";
            return RedirectToPage();
        }

        var domainName = cert.Domain?.DomainName;
        _db.SslCertificates.Remove(cert);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "SslCertificate", id.ToString(), cert.DomainId.ToString());

        if (!string.IsNullOrEmpty(domainName)) await _nginx.DisableSslAsync(domainName);
        TempData["Success"] = "Certificate has been removed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRenewAsync(int id)
    {
        var cert = await _db.SslCertificates.Include(c => c.Domain).FirstOrDefaultAsync(c => c.Id == id);
        if (cert == null || !await _scope.CanManageUserAsync(User, cert.UserId))
        {
            TempData["Error"] = "Certificate not found or access denied.";
            return RedirectToPage();
        }

        // Delegate to certbot (simulated on dev); the service updates the entity.
        var renew = await _ssl.RenewCertificateAsync(cert.Domain?.DomainName ?? "");
        await _audit.LogAsync("Renew", "SslCertificate", id.ToString(), cert.Domain?.DomainName);

        // Reload the entity to reflect the new expiry.
        await _db.Entry(cert).ReloadAsync();
        var suffix = renew.Simulated ? " (certbot renew simulated)" : "";
        TempData["Success"] = $"Certificate for '{cert.Domain?.DomainName}' renewed until {cert.ExpiresAt:yyyy-MM-dd}.{suffix}";
        return RedirectToPage();
    }
}
