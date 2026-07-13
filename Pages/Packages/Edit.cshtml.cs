using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Data;
using SRXPanel.Services;

namespace SRXPanel.Pages.Packages;

public class EditModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditLogService _auditLog;

    public EditModel(ApplicationDbContext db, IAuditLogService auditLog)
    {
        _db = db;
        _auditLog = auditLog;
    }

    [BindProperty]
    public PackageInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var package = await _db.Packages.FindAsync(Id);
        if (package == null)
        {
            return NotFound();
        }

        Input = new PackageInput
        {
            Name = package.Name,
            DiskQuotaMB = package.DiskQuotaMB,
            BandwidthQuotaMB = package.BandwidthQuotaMB,
            MaxDomains = package.MaxDomains,
            MaxEmails = package.MaxEmails,
            MaxDatabases = package.MaxDatabases,
            MaxFtpAccounts = package.MaxFtpAccounts,
            MaxCronJobs = package.MaxCronJobs,
            MaxBackups = package.MaxBackups,
            Price = package.Price,
            AllowVpsStore = package.AllowVpsStore,
            AllowAppHosting = package.AllowAppHosting,
            AllowCloudflare = package.AllowCloudflare,
            AllowAdvancedMail = package.AllowAdvancedMail,
            AllowDeveloperTools = package.AllowDeveloperTools
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var package = await _db.Packages.FindAsync(Id);
        if (package == null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        package.Name = Input.Name;
        package.DiskQuotaMB = Input.DiskQuotaMB;
        package.BandwidthQuotaMB = Input.BandwidthQuotaMB;
        package.MaxDomains = Input.MaxDomains;
        package.MaxEmails = Input.MaxEmails;
        package.MaxDatabases = Input.MaxDatabases;
        package.MaxFtpAccounts = Input.MaxFtpAccounts;
        package.MaxCronJobs = Input.MaxCronJobs;
        package.MaxBackups = Input.MaxBackups;
        package.Price = Input.Price;
        package.AllowVpsStore = Input.AllowVpsStore;
        package.AllowAppHosting = Input.AllowAppHosting;
        package.AllowCloudflare = Input.AllowCloudflare;
        package.AllowAdvancedMail = Input.AllowAdvancedMail;
        package.AllowDeveloperTools = Input.AllowDeveloperTools;

        await _db.SaveChangesAsync();

        await _auditLog.LogAsync("Update", "Package", package.Id.ToString(), package.Name);
        TempData["Success"] = $"Package '{package.Name}' updated.";
        return RedirectToPage("/Packages/Index");
    }
}
