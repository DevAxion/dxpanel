using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Pages.Client;

public class SslRow
{
    public Domain Domain { get; set; } = null!;
    public SslCertificate? Cert { get; set; }
}

public class SslModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _audit;
    private readonly ISslService _ssl;
    private readonly INginxService _nginx;
    private readonly PanelSettings _panel;

    public SslModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IAuditLogService audit,
        ISslService ssl, INginxService nginx, IOptionsMonitor<PanelSettings> panel)
    {
        _db = db;
        _userManager = userManager;
        _audit = audit;
        _ssl = ssl;
        _nginx = nginx;
        _panel = panel.CurrentValue;
    }

    public List<SslRow> Rows { get; set; } = new();

    private async Task<ApplicationUser?> LoadAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return null;
        var domains = await _db.Domains.Where(d => d.UserId == user.Id).OrderBy(d => d.DomainName).ToListAsync();
        var certs = await _db.SslCertificates.Where(c => c.UserId == user.Id).ToListAsync();
        Rows = domains.Select(d => new SslRow { Domain = d, Cert = certs.FirstOrDefault(c => c.DomainId == d.Id) }).ToList();
        return user;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (await LoadAsync() == null) return Challenge();
        return Page();
    }

    private async Task<Domain?> OwnedDomainAsync(int id, string userId) =>
        await _db.Domains.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);

    public async Task<IActionResult> OnPostIssueAsync(int domainId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var domain = await OwnedDomainAsync(domainId, user.Id);
        if (domain == null) { TempData["Error"] = "Domain not found."; return RedirectToPage(); }

        var result = await _ssl.IssueLetsEncryptAsync(domain.DomainName, _panel.LetsEncryptEmail);
        await _audit.LogAsync("Issue", "SslCertificate", null, $"LetsEncrypt {domain.DomainName}");
        var cert = await _db.SslCertificates.FirstOrDefaultAsync(c => c.DomainId == domain.Id);
        if (cert?.CertificatePath != null && cert.KeyPath != null)
            await _nginx.EnableSslAsync(domain.DomainName, cert.CertificatePath, cert.KeyPath);
        TempData["Success"] = result.Simulated ? $"Let's Encrypt certificate issued for {domain.DomainName} (simulated)." : $"Certificate issued for {domain.DomainName}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAutoRenewAsync(int domainId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var domain = await OwnedDomainAsync(domainId, user.Id);
        if (domain == null) { TempData["Error"] = "Domain not found."; return RedirectToPage(); }
        domain.AutoRenewSsl = !domain.AutoRenewSsl;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Auto-renew {(domain.AutoRenewSsl ? "enabled" : "disabled")} for {domain.DomainName}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostForceHttpsAsync(int domainId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var domain = await OwnedDomainAsync(domainId, user.Id);
        if (domain == null) { TempData["Error"] = "Domain not found."; return RedirectToPage(); }
        domain.ForceHttps = !domain.ForceHttps;
        await _db.SaveChangesAsync();
        await _nginx.CreateVirtualHostAsync(domain.DomainName, domain.DocumentRoot, domain.PhpVersion);
        TempData["Success"] = $"Force HTTPS {(domain.ForceHttps ? "enabled" : "disabled")} for {domain.DomainName}.";
        return RedirectToPage();
    }
}
