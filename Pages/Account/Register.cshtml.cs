using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Reseller;

namespace SRXPanel.Pages.Account;

[AllowAnonymous]
public class RegisterModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IAuditLogService _audit;
    private readonly IAffiliateService _affiliates;

    public RegisterModel(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager,
        IAuditLogService audit, IAffiliateService affiliates)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _audit = audit;
        _affiliates = affiliates;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public class InputModel
    {
        [Required]
        [StringLength(64, MinimumLength = 3)]
        [RegularExpression("^[a-zA-Z0-9_.-]+$", ErrorMessage = "Letters, numbers, and . _ - only.")]
        public string UserName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = 8)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        [Display(Name = "Confirm Password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var user = new ApplicationUser
        {
            UserName = Input.UserName,
            Email = Input.Email,
            FullName = Input.FullName,
            EmailConfirmed = true,
            IsActive = true,
            DiskQuotaMB = 0,
            BandwidthQuotaMB = 0,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, Input.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return Page();
        }

        await _userManager.AddToRoleAsync(user, Roles.Client);
        await _audit.LogAsync("Register", "User", user.Id, user.UserName);

        // Attribute the signup to an affiliate if a referral cookie is present.
        var refCode = Request.Cookies["srx_ref"];
        if (!string.IsNullOrEmpty(refCode))
        {
            var affiliate = await _affiliates.GetByCodeAsync(refCode);
            if (affiliate != null && affiliate.UserId != user.Id)
            {
                var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                await _affiliates.RecordSignupAsync(affiliate.Id, user.Id, ip);
                user.ReferredByAffiliateId = affiliate.Id;
                await _userManager.UpdateAsync(user);
            }
            Response.Cookies.Delete("srx_ref");
        }

        await _signInManager.SignInAsync(user, isPersistent: false);

        if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
        {
            return LocalRedirect(ReturnUrl);
        }
        return RedirectToPage("/Dashboard/Index");
    }
}
