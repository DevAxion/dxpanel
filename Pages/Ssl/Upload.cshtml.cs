using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;

namespace SRXPanel.Pages.Ssl;

public class UploadModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserScopeService _scope;
    private readonly IAuditLogService _audit;
    private readonly IWebHostEnvironment _env;

    public UploadModel(ApplicationDbContext db, IUserScopeService scope, IAuditLogService audit, IWebHostEnvironment env)
    {
        _db = db;
        _scope = scope;
        _audit = audit;
        _env = env;
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
        [Display(Name = "Certificate (PEM)")]
        public string CertificatePem { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Private Key (PEM)")]
        public string PrivateKeyPem { get; set; } = string.Empty;

        [Display(Name = "Expires At")]
        [DataType(DataType.Date)]
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddYears(1);
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

        if (!Input.CertificatePem.Contains("BEGIN CERTIFICATE"))
        {
            ModelState.AddModelError(nameof(Input.CertificatePem), "Certificate does not look like a valid PEM.");
            return Page();
        }
        if (!Input.PrivateKeyPem.Contains("PRIVATE KEY"))
        {
            ModelState.AddModelError(nameof(Input.PrivateKeyPem), "Private key does not look like a valid PEM.");
            return Page();
        }

        var certDir = Path.Combine(_env.ContentRootPath, "App_Data", "ssl", domain.DomainName);
        Directory.CreateDirectory(certDir);
        var certPath = Path.Combine(certDir, "fullchain.pem");
        var keyPath = Path.Combine(certDir, "privkey.pem");
        await System.IO.File.WriteAllTextAsync(certPath, Input.CertificatePem);
        await System.IO.File.WriteAllTextAsync(keyPath, Input.PrivateKeyPem);

        var existing = await _db.SslCertificates.Where(c => c.DomainId == domain.Id).ToListAsync();
        _db.SslCertificates.RemoveRange(existing);

        var cert = new SslCertificate
        {
            DomainId = domain.Id,
            UserId = domain.UserId,
            Type = SslCertType.Custom,
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.SpecifyKind(Input.ExpiresAt, DateTimeKind.Utc),
            Status = SslCertStatus.Active,
            CertificatePath = certPath,
            KeyPath = keyPath
        };
        _db.SslCertificates.Add(cert);
        domain.SslEnabled = true;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Upload", "SslCertificate", cert.Id.ToString(), domain.DomainName);

        TempData["Success"] = $"Custom certificate uploaded for '{domain.DomainName}'.";
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
