using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace SRXPanel.Pages.Ssl;

public class IssueModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserScopeService _scope;
    private readonly IRateLimitService _rateLimit;
    private readonly IAuditLogService _audit;
    private readonly ISslService _ssl;
    private readonly INginxService _nginx;
    private readonly PanelSettings _settings;

    public IssueModel(ApplicationDbContext db, IUserScopeService scope, IRateLimitService rateLimit, IAuditLogService audit,
        ISslService ssl, INginxService nginx, IOptionsMonitor<PanelSettings> settings)
    {
        _db = db;
        _scope = scope;
        _rateLimit = rateLimit;
        _audit = audit;
        _ssl = ssl;
        _nginx = nginx;
        _settings = settings.CurrentValue;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<SelectListItem> DomainOptions { get; set; } = new();

    public class InputModel
    {
        [Required]
        [Display(Name = "Domain")]
        public int DomainId { get; set; }

        [Required]
        [Display(Name = "Certificate Type")]
        public SslCertType Type { get; set; } = SslCertType.LetsEncrypt;
    }

    public async Task OnGetAsync()
    {
        await LoadOptionsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadOptionsAsync();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var domain = await _db.Domains.FirstOrDefaultAsync(d => d.Id == Input.DomainId);
        if (domain == null || !await _scope.CanManageUserAsync(User, domain.UserId))
        {
            ModelState.AddModelError(nameof(Input.DomainId), "Select a domain you manage.");
            return Page();
        }

        if (!_rateLimit.IsAllowed(domain.UserId, "create"))
        {
            TempData["Error"] = "Rate limit reached. Please wait a minute before creating more resources.";
            return RedirectToPage("/Ssl/Index");
        }

        // Delegate to the SSL service — it runs certbot/openssl (simulated on dev)
        // AND upserts the SslCertificate entity in the DB.
        var sslResult = Input.Type == SslCertType.LetsEncrypt
            ? await _ssl.IssueLetsEncryptAsync(domain.DomainName, _settings.LetsEncryptEmail)
            : await _ssl.IssueSelfSignedAsync(domain.DomainName);

        await _audit.LogAsync("Issue", "SslCertificate", null, $"{domain.DomainName} ({Input.Type})");

        // Point nginx at the new certificate.
        var cert = await _db.SslCertificates.FirstOrDefaultAsync(c => c.DomainId == domain.Id);
        if (cert?.CertificatePath != null && cert.KeyPath != null)
        {
            await _nginx.EnableSslAsync(domain.DomainName, cert.CertificatePath, cert.KeyPath);
        }

        var suffix = sslResult.Simulated ? " (certbot/openssl simulated)" : sslResult.Success ? "" : $" Warning: {sslResult.Message}";
        TempData[sslResult.Success ? "Success" : "Error"] = $"{Input.Type} certificate issued for '{domain.DomainName}'.{suffix}";
        return RedirectToPage("/Ssl/Index");
    }

    private async Task LoadOptionsAsync()
    {
        var manageable = await _scope.GetManageableUserIdsAsync(User);
        DomainOptions = await _db.Domains
            .Where(d => manageable.Contains(d.UserId))
            .OrderBy(d => d.DomainName)
            .Select(d => new SelectListItem(d.DomainName, d.Id.ToString()))
            .ToListAsync();
    }
}
