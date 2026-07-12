using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;

namespace SRXPanel.Pages.Users;

public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _auditLog;

    public CreateModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IAuditLogService auditLog)
    {
        _db = db;
        _userManager = userManager;
        _auditLog = auditLog;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool IsSuperAdmin { get; set; }
    public List<SelectListItem> ResellerOptions { get; set; } = new();
    public List<SelectListItem> PackageOptions { get; set; } = new();

    public class InputModel
    {
        [Required]
        [StringLength(50, MinimumLength = 3)]
        public string UserName { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Role")]
        public string Role { get; set; } = Roles.Client;

        [Display(Name = "Reseller")]
        public string? ResellerId { get; set; }

        [Display(Name = "Package")]
        public int? PackageId { get; set; }

        [Display(Name = "Disk Quota (MB)")]
        [Range(0, long.MaxValue)]
        public long DiskQuotaMB { get; set; } = 1024;

        [Display(Name = "Bandwidth Quota (MB)")]
        [Range(0, long.MaxValue)]
        public long BandwidthQuotaMB { get; set; } = 10240;
    }

    public async Task OnGetAsync()
    {
        await LoadOptionsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        IsSuperAdmin = User.IsInRole(Roles.SuperAdmin);

        if (!IsSuperAdmin)
        {
            // Resellers can only create Clients under themselves
            Input.Role = Roles.Client;
            var currentUser = await _userManager.GetUserAsync(User);
            Input.ResellerId = currentUser?.Id;
        }

        if (!ModelState.IsValid)
        {
            await LoadOptionsAsync();
            return Page();
        }

        var package = Input.PackageId.HasValue ? await _db.Packages.FindAsync(Input.PackageId.Value) : null;

        var user = new ApplicationUser
        {
            UserName = Input.UserName,
            Email = Input.Email,
            FullName = Input.FullName,
            EmailConfirmed = true,
            IsActive = true,
            ResellerId = Input.Role == Roles.Client ? Input.ResellerId : null,
            PackageId = package?.Id,
            DiskQuotaMB = package?.DiskQuotaMB ?? Input.DiskQuotaMB,
            BandwidthQuotaMB = package?.BandwidthQuotaMB ?? Input.BandwidthQuotaMB,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, Input.Password);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            await LoadOptionsAsync();
            return Page();
        }

        await _userManager.AddToRoleAsync(user, Input.Role);
        await _auditLog.LogAsync("Create", "User", user.Id, $"{user.UserName} ({Input.Role})");

        TempData["Success"] = $"User '{user.UserName}' created successfully.";
        return RedirectToPage("/Users/Index");
    }

    private async Task LoadOptionsAsync()
    {
        IsSuperAdmin = User.IsInRole(Roles.SuperAdmin);

        if (IsSuperAdmin)
        {
            var resellers = await _userManager.GetUsersInRoleAsync(Roles.Reseller);
            ResellerOptions = resellers.Select(r => new SelectListItem(r.UserName, r.Id)).ToList();
        }

        PackageOptions = await _db.Packages
            .Select(p => new SelectListItem($"{p.Name} ({p.DiskQuotaMB} MB)", p.Id.ToString()))
            .ToListAsync();
    }
}
