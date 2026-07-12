using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Security;

namespace SRXPanel.Pages.Account;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _auditLog;
    private readonly IBruteForceService _bruteForce;

    public LoginModel(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager,
        IAuditLogService auditLog, IBruteForceService bruteForce)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _auditLog = auditLog;
        _bruteForce = bruteForce;
    }

    private string ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private Task RecordAsync(bool success) => _bruteForce.RecordAttemptAsync(
        ClientIp, LoginAttemptType.Panel, Input.UserNameOrEmail, success, Request.Headers.UserAgent.ToString());

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }

    public class InputModel
    {
        [Required]
        [Display(Name = "Username or Email")]
        public string UserNameOrEmail { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }
    }

    /// <summary>External auth schemes (e.g. Google) that are actually configured.</summary>
    public IList<AuthenticationScheme> ExternalLogins { get; set; } = new List<AuthenticationScheme>();

    public async Task OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
    }

    // Kick off an external login (Google). The provider handler redirects to
    // its consent screen, then back to the Callback handler below.
    public IActionResult OnPostExternalLogin(string provider, string? returnUrl = null)
    {
        var redirectUrl = Url.Page("./Login", pageHandler: "Callback", values: new { returnUrl });
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return new ChallengeResult(provider, properties);
    }

    public async Task<IActionResult> OnGetCallbackAsync(string? returnUrl = null, string? remoteError = null)
    {
        returnUrl ??= Url.Content("~/Dashboard/Index");
        ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

        if (remoteError != null)
        {
            ModelState.AddModelError(string.Empty, $"Error from external provider: {remoteError}");
            return Page();
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            ModelState.AddModelError(string.Empty, "Error loading external login information.");
            return Page();
        }

        // Already linked → sign in directly.
        var signInResult = await _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);
        if (signInResult.Succeeded)
        {
            await _auditLog.LogAsync("Login", "User", info.Principal.Identity?.Name ?? "external");
            return LocalRedirect(returnUrl);
        }
        if (signInResult.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "This account is locked out. Try again later.");
            return Page();
        }

        // Not linked yet: link to an existing account with the same verified email.
        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrEmpty(email))
        {
            ModelState.AddModelError(string.Empty, "The external provider did not supply an email address.");
            return Page();
        }

        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
        {
            ModelState.AddModelError(string.Empty,
                "No panel account matches this Google email. Please contact your administrator to have an account created, then sign in.");
            return Page();
        }

        if (!user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "This account has been suspended.");
            return Page();
        }

        var linkResult = await _userManager.AddLoginAsync(user, info);
        if (!linkResult.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Could not link the external login to your account.");
            return Page();
        }

        await _signInManager.SignInAsync(user, isPersistent: false);
        await _auditLog.LogAsync("Login", "User", user.Id);
        return LocalRedirect(returnUrl);
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/Dashboard/Index");

        if (!ModelState.IsValid)
        {
            return Page();
        }

        // Brute-force protection: reject requests from blocked IPs outright.
        if (await _bruteForce.IsBlockedAsync(ClientIp))
        {
            ModelState.AddModelError(string.Empty, "Your IP address is temporarily blocked due to suspicious activity. Try again later.");
            return Page();
        }

        var user = await _userManager.FindByNameAsync(Input.UserNameOrEmail)
                   ?? await _userManager.FindByEmailAsync(Input.UserNameOrEmail);

        if (user == null)
        {
            await RecordAsync(false);
            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            return Page();
        }

        if (!user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "This account has been suspended.");
            return Page();
        }

        var result = await _signInManager.PasswordSignInAsync(user.UserName!, Input.Password, Input.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            await RecordAsync(true);
            await _auditLog.LogAsync("Login", "User", user.Id);
            return LocalRedirect(returnUrl);
        }

        await RecordAsync(false);

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "This account is locked out. Try again later.");
            return Page();
        }

        ModelState.AddModelError(string.Empty, "Invalid login attempt.");
        return Page();
    }
}
