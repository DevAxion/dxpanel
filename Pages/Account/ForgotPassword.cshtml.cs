using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Billing;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Pages.Account;

[AllowAnonymous]
public class ForgotPasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMailerService _mailer;
    private readonly IAuditLogService _audit;
    private readonly ICommandRunner _runner;
    private readonly ILogger<ForgotPasswordModel> _logger;

    public ForgotPasswordModel(UserManager<ApplicationUser> userManager, IMailerService mailer,
        IAuditLogService audit, ICommandRunner runner, ILogger<ForgotPasswordModel> logger)
    {
        _userManager = userManager;
        _mailer = mailer;
        _audit = audit;
        _runner = runner;
        _logger = logger;
    }

    [BindProperty] public InputModel Input { get; set; } = new();

    /// <summary>Set after a successful post so the view shows the confirmation panel.</summary>
    public bool Sent { get; private set; }

    public class InputModel
    {
        [Required, EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        // Always show the same confirmation regardless of whether the account
        // exists, so this endpoint can't be used to enumerate registered emails.
        Sent = true;

        var user = await _userManager.FindByEmailAsync(Input.Email);
        if (user == null || !user.IsActive)
            return Page();

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        var resetUrl = Url.Page("/Account/ResetPassword", pageHandler: null,
            values: new { code, email = user.Email }, protocol: Request.Scheme)!;

        await _mailer.SendTemplateAsync(user.Email!, "Reset your SRXPanel password", "reset_password",
            new Dictionary<string, string>
            {
                ["NAME"] = string.IsNullOrWhiteSpace(user.FullName) ? user.UserName ?? "there" : user.FullName,
                ["USERNAME"] = user.UserName ?? "",
                ["RESET_URL"] = resetUrl
            });

        await _audit.LogAsync("PasswordResetRequested", "User", user.Id, user.UserName);
        _logger.LogInformation("Password reset requested for {UserId}", user.Id);

        // In simulation (no real SMTP) the email isn't delivered — surface the reset
        // link in the log so an operator who hasn't configured SMTP yet can still use it.
        if (_runner.SimulationMode)
            _logger.LogWarning("[SIMULATION] Password reset link for {Email}: {ResetUrl}", user.Email, resetUrl);

        return Page();
    }
}
