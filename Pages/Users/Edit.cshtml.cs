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

public class EditModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _auditLog;

    public EditModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IAuditLogService auditLog)
    {
        _db = db;
        _userManager = userManager;
        _auditLog = auditLog;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = string.Empty;

    public List<SelectListItem> PackageOptions { get; set; } = new();

    public class InputModel
    {
        [Required]
        [StringLength(150)]
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Display(Name = "Package")]
        public int? PackageId { get; set; }

        [Display(Name = "Disk Quota (MB)")]
        [Range(0, long.MaxValue)]
        public long DiskQuotaMB { get; set; }

        [Display(Name = "Bandwidth Quota (MB)")]
        [Range(0, long.MaxValue)]
        public long BandwidthQuotaMB { get; set; }

        [Display(Name = "New Password (leave blank to keep current)")]
        [DataType(DataType.Password)]
        public string? NewPassword { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.FindByIdAsync(Id);
        if (user == null || !await CanManageUserAsync(user))
        {
            return NotFound();
        }

        Input = new InputModel
        {
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            PackageId = user.PackageId,
            DiskQuotaMB = user.DiskQuotaMB,
            BandwidthQuotaMB = user.BandwidthQuotaMB
        };

        await LoadOptionsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _userManager.FindByIdAsync(Id);
        if (user == null || !await CanManageUserAsync(user))
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            await LoadOptionsAsync();
            return Page();
        }

        var package = Input.PackageId.HasValue ? await _db.Packages.FindAsync(Input.PackageId.Value) : null;

        user.FullName = Input.FullName;
        user.Email = Input.Email;
        user.PackageId = package?.Id;
        user.DiskQuotaMB = package?.DiskQuotaMB ?? Input.DiskQuotaMB;
        user.BandwidthQuotaMB = package?.BandwidthQuotaMB ?? Input.BandwidthQuotaMB;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            await LoadOptionsAsync();
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(Input.NewPassword))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var pwResult = await _userManager.ResetPasswordAsync(user, token, Input.NewPassword);
            if (!pwResult.Succeeded)
            {
                foreach (var error in pwResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                await LoadOptionsAsync();
                return Page();
            }
        }

        await _auditLog.LogAsync("Update", "User", user.Id, user.UserName);
        TempData["Success"] = $"User '{user.UserName}' updated successfully.";
        return RedirectToPage("/Users/Index");
    }

    private async Task<bool> CanManageUserAsync(ApplicationUser target)
    {
        if (User.IsInRole(Roles.SuperAdmin))
        {
            return true;
        }

        if (User.IsInRole(Roles.Reseller))
        {
            var currentUser = await _userManager.GetUserAsync(User);
            return currentUser != null && target.ResellerId == currentUser.Id;
        }

        return false;
    }

    private async Task LoadOptionsAsync()
    {
        PackageOptions = await _db.Packages
            .Select(p => new SelectListItem($"{p.Name} ({p.DiskQuotaMB} MB)", p.Id.ToString()))
            .ToListAsync();
    }
}
