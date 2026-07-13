using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;

namespace SRXPanel.Pages.Packages;

public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditLogService _auditLog;

    public CreateModel(ApplicationDbContext db, IAuditLogService auditLog)
    {
        _db = db;
        _auditLog = auditLog;
    }

    [BindProperty]
    public PackageInput Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var package = new Package
        {
            Name = Input.Name,
            DiskQuotaMB = Input.DiskQuotaMB,
            BandwidthQuotaMB = Input.BandwidthQuotaMB,
            MaxDomains = Input.MaxDomains,
            MaxEmails = Input.MaxEmails,
            MaxDatabases = Input.MaxDatabases,
            MaxFtpAccounts = Input.MaxFtpAccounts,
            MaxCronJobs = Input.MaxCronJobs,
            MaxBackups = Input.MaxBackups,
            Price = Input.Price,
            AllowVpsStore = Input.AllowVpsStore,
            AllowAppHosting = Input.AllowAppHosting,
            AllowCloudflare = Input.AllowCloudflare,
            AllowAdvancedMail = Input.AllowAdvancedMail,
            AllowDeveloperTools = Input.AllowDeveloperTools
        };

        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        await _auditLog.LogAsync("Create", "Package", package.Id.ToString(), package.Name);
        TempData["Success"] = $"Package '{package.Name}' created.";
        return RedirectToPage("/Packages/Index");
    }
}

public class PackageInput
{
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Disk Quota (MB, 0 = unlimited)")]
    [Range(0, long.MaxValue)]
    public long DiskQuotaMB { get; set; }

    [Display(Name = "Bandwidth Quota (MB, 0 = unlimited)")]
    [Range(0, long.MaxValue)]
    public long BandwidthQuotaMB { get; set; }

    [Display(Name = "Max Domains (0 = unlimited)")]
    [Range(0, int.MaxValue)]
    public int MaxDomains { get; set; }

    [Display(Name = "Max Emails (0 = unlimited)")]
    [Range(0, int.MaxValue)]
    public int MaxEmails { get; set; }

    [Display(Name = "Max Databases (0 = unlimited)")]
    [Range(0, int.MaxValue)]
    public int MaxDatabases { get; set; }

    [Display(Name = "Max FTP Accounts (0 = unlimited)")]
    [Range(0, int.MaxValue)]
    public int MaxFtpAccounts { get; set; }

    [Display(Name = "Max Cron Jobs (0 = unlimited)")]
    [Range(0, int.MaxValue)]
    public int MaxCronJobs { get; set; } = 10;

    [Display(Name = "Max Backups (0 = unlimited)")]
    [Range(0, int.MaxValue)]
    public int MaxBackups { get; set; } = 1;

    [Range(0, double.MaxValue)]
    public decimal Price { get; set; }

    // ---- Feature flags (gate Client sidebar sections) ----
    [Display(Name = "VPS Store")]
    public bool AllowVpsStore { get; set; } = true;

    [Display(Name = "App Hosting (Hosted Apps)")]
    public bool AllowAppHosting { get; set; } = true;

    [Display(Name = "Cloudflare")]
    public bool AllowCloudflare { get; set; } = true;

    [Display(Name = "Advanced Mail (Mail Server, Deliverability)")]
    public bool AllowAdvancedMail { get; set; } = true;

    [Display(Name = "Developer Tools")]
    public bool AllowDeveloperTools { get; set; } = true;
}
