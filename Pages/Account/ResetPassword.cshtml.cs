using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using SRXPanel.Models;
using SRXPanel.Services;

namespace SRXPanel.Pages.Account;

[AllowAnonymous]
public class ResetPasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _audit;

    public ResetPasswordModel(UserManager<ApplicationUser> userManager, IAuditLogService audit)
    {
        _userManager = userManager;
        _audit = audit;
    }

    [BindProperty] public InputModel Input { get; set; } = new();

    /// <summary>Set once the password has been reset so the view shows the success panel.</summary>
    public bool Done { get; private set; }

    public class InputModel
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;

        // The reset token, Base64Url-encoded in the emailed link.
        [Required] public string Code { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public IActionResult OnGet(string? code = null, string? email = null)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(email))
        {
            ModelState.AddModelError(string.Empty, "This password reset link is invalid or incomplete.");
            return Page();
        }

        Input.Code = code;
        Input.Email = email;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var user = await _userManager.FindByEmailAsync(Input.Email);
        if (user == null)
        {
            // Don't reveal whether the account exists — show the generic success panel.
            Done = true;
            return Page();
        }

        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Input.Code));
        }
        catch (FormatException)
        {
            ModelState.AddModelError(string.Empty, "This password reset link is invalid or has expired.");
            return Page();
        }

        var result = await _userManager.ResetPasswordAsync(user, token, Input.Password);
        if (result.Succeeded)
        {
            await _audit.LogAsync("PasswordReset", "User", user.Id, user.UserName);
            Done = true;
            return Page();
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);
        return Page();
    }
}
