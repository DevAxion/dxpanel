using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services;

namespace SRXPanel.Pages.Account;

[AllowAnonymous]
public class LoginWith2faModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IAuditLogService _audit;

    public LoginWith2faModel(SignInManager<ApplicationUser> signInManager, IAuditLogService audit)
    {
        _signInManager = signInManager;
        _audit = audit;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty(SupportsGet = true)] public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }

    public class InputModel
    {
        [Required]
        [DataType(DataType.Text)]
        [Display(Name = "Authenticator code")]
        public string TwoFactorCode { get; set; } = string.Empty;

        [Display(Name = "Remember this device")]
        public bool RememberMachine { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        // There must be a user who has passed the first (password) step.
        if (await _signInManager.GetTwoFactorAuthenticationUserAsync() == null)
            return RedirectToPage("./Login");

        ReturnUrl = returnUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/Dashboard/Index");

        var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user == null)
            return RedirectToPage("./Login");

        if (!ModelState.IsValid)
        {
            ReturnUrl = returnUrl;
            return Page();
        }

        var code = Input.TwoFactorCode.Replace(" ", "").Replace("-", "");
        var result = await _signInManager.TwoFactorAuthenticatorSignInAsync(code, RememberMe, Input.RememberMachine);

        if (result.Succeeded)
        {
            await _audit.LogAsync("Login2FA", "User", user.Id, user.UserName);
            return LocalRedirect(returnUrl);
        }
        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "This account is locked out. Try again later.");
            ReturnUrl = returnUrl;
            return Page();
        }

        ModelState.AddModelError(string.Empty, "Invalid authenticator code.");
        ReturnUrl = returnUrl;
        return Page();
    }
}
