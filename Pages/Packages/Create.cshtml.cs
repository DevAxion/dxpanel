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
            Price = Input.Price
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
}
