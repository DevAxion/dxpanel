using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Reseller;

namespace SRXPanel.Pages.Reseller;

public class PackagesModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IResellerService _resellers;
    private readonly IAuditLogService _audit;

    public PackagesModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        IResellerService resellers, IAuditLogService audit)
    {
        _db = db;
        _userManager = userManager;
        _resellers = resellers;
        _audit = audit;
    }

    public ResellerProfile? Profile { get; set; }
    public List<ResellerPackage> Packages { get; set; } = new();

    [BindProperty] public InputModel Input { get; set; } = new();

    public class InputModel
    {
        public int Id { get; set; }
        [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
        [StringLength(300)] public string? Description { get; set; }
        [Range(0, long.MaxValue)] public long DiskQuotaMB { get; set; } = 1024;
        [Range(0, long.MaxValue)] public long BandwidthQuotaMB { get; set; } = 10240;
        [Range(0, int.MaxValue)] public int MaxDomains { get; set; } = 1;
        [Range(0, int.MaxValue)] public int MaxEmails { get; set; } = 5;
        [Range(0, int.MaxValue)] public int MaxDatabases { get; set; } = 2;
        [Range(0, int.MaxValue)] public int MaxFtpAccounts { get; set; } = 2;
        [Range(0, 100000)] public decimal Price { get; set; }
        public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;
        public bool IsPublic { get; set; } = true;
    }

    private async Task<ApplicationUser?> LoadAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return null;
        Profile = await _resellers.GetProfileAsync(user.Id);
        // SQLite cannot ORDER BY decimal; sort in memory after materializing.
        Packages = (await _db.ResellerPackages
            .Where(p => p.ResellerId == user.Id)
            .ToListAsync())
            .OrderBy(p => p.Price).ToList();
        return user;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (await LoadAsync() == null) return Challenge();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var user = await LoadAsync();
        if (user == null) return Challenge();
        if (Profile == null) { TempData["Error"] = "No reseller allocation configured."; return RedirectToPage(); }

        if (!ModelState.IsValid) { return Page(); }

        var draft = new ResellerPackage
        {
            ResellerId = user.Id,
            Name = Input.Name.Trim(),
            Description = Input.Description,
            DiskQuotaMB = Input.DiskQuotaMB,
            BandwidthQuotaMB = Input.BandwidthQuotaMB,
            MaxDomains = Input.MaxDomains,
            MaxEmails = Input.MaxEmails,
            MaxDatabases = Input.MaxDatabases,
            MaxFtpAccounts = Input.MaxFtpAccounts,
            Price = Input.Price,
            BillingCycle = Input.BillingCycle,
            IsPublic = Input.IsPublic
        };

        var (ok, error) = await _resellers.ValidatePackageAsync(Profile, draft);
        if (!ok) { TempData["Error"] = error; return RedirectToPage(); }

        if (Input.Id > 0)
        {
            var existing = await _db.ResellerPackages
                .FirstOrDefaultAsync(p => p.Id == Input.Id && p.ResellerId == user.Id);
            if (existing == null) { TempData["Error"] = "Package not found."; return RedirectToPage(); }

            existing.Name = draft.Name;
            existing.Description = draft.Description;
            existing.DiskQuotaMB = draft.DiskQuotaMB;
            existing.BandwidthQuotaMB = draft.BandwidthQuotaMB;
            existing.MaxDomains = draft.MaxDomains;
            existing.MaxEmails = draft.MaxEmails;
            existing.MaxDatabases = draft.MaxDatabases;
            existing.MaxFtpAccounts = draft.MaxFtpAccounts;
            existing.Price = draft.Price;
            existing.BillingCycle = draft.BillingCycle;
            existing.IsPublic = draft.IsPublic;
            await _db.SaveChangesAsync();
            await _audit.LogAsync("Update", "ResellerPackage", existing.Id.ToString(), existing.Name);
            TempData["Success"] = $"Package '{existing.Name}' updated.";
        }
        else
        {
            _db.ResellerPackages.Add(draft);
            await _db.SaveChangesAsync();
            await _audit.LogAsync("Create", "ResellerPackage", draft.Id.ToString(), draft.Name);
            TempData["Success"] = $"Package '{draft.Name}' created.";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var pkg = await _db.ResellerPackages.FirstOrDefaultAsync(p => p.Id == id && p.ResellerId == user.Id);
        if (pkg == null) { TempData["Error"] = "Package not found."; return RedirectToPage(); }
        pkg.IsPublic = !pkg.IsPublic;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Package '{pkg.Name}' is now {(pkg.IsPublic ? "public" : "private")}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var pkg = await _db.ResellerPackages.FirstOrDefaultAsync(p => p.Id == id && p.ResellerId == user.Id);
        if (pkg == null) { TempData["Error"] = "Package not found."; return RedirectToPage(); }

        var inUse = await _db.Users.AnyAsync(u => u.ResellerPackageId == id);
        if (inUse) { TempData["Error"] = "Cannot delete a package that is assigned to clients."; return RedirectToPage(); }

        _db.ResellerPackages.Remove(pkg);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "ResellerPackage", id.ToString(), pkg.Name);
        TempData["Success"] = $"Package '{pkg.Name}' deleted.";
        return RedirectToPage();
    }
}
