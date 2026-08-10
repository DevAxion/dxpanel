using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QRCoder;
using SRXPanel.Models;
using SRXPanel.Services;

namespace SRXPanel.Pages.Account;

[Authorize]

/// <summary>
/// Two-factor authentication (TOTP authenticator app) management for the signed-in
/// user: view status, enable via QR code + verification, view/regenerate recovery
/// codes, and disable.
/// </summary>
public class TwoFactorModel : PageModel
{
    private const string Issuer = "SRXPanel";
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _audit;
    private readonly UrlEncoder _urlEncoder;

    public TwoFactorModel(UserManager<ApplicationUser> userManager, IAuditLogService audit, UrlEncoder urlEncoder)
    {
        _userManager = userManager;
        _audit = audit;
        _urlEncoder = urlEncoder;
    }

    public bool Is2faEnabled { get; private set; }
    public int RecoveryCodesLeft { get; private set; }

    // Set only while the user is in the "set up authenticator" step.
    public bool ShowSetup { get; private set; }
    public string SharedKey { get; private set; } = "";
    public string QrCodeDataUri { get; private set; } = "";

    // Set right after enabling / regenerating so the codes are shown exactly once.
    public string[]? RecoveryCodes { get; private set; }

    [BindProperty] public string? VerificationCode { get; set; }

    private async Task LoadAsync(ApplicationUser user)
    {
        Is2faEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
        RecoveryCodesLeft = await _userManager.CountRecoveryCodesAsync(user);
    }

    public async Task<IActionResult> OnGetAsync(bool setup = false)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        await LoadAsync(user);

        if (setup && !Is2faEnabled)
            await BuildSetupAsync(user);

        return Page();
    }

    public async Task<IActionResult> OnPostEnableAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        await LoadAsync(user);

        var code = (VerificationCode ?? "").Replace(" ", "").Replace("-", "");
        var valid = await _userManager.VerifyTwoFactorTokenAsync(
            user, _userManager.Options.Tokens.AuthenticatorTokenProvider, code);

        if (!valid)
        {
            ModelState.AddModelError(string.Empty, "That verification code is invalid. Make sure your device clock is correct and try again.");
            await BuildSetupAsync(user);
            return Page();
        }

        await _userManager.SetTwoFactorEnabledAsync(user, true);
        await _audit.LogAsync("Enable2FA", "User", user.Id, user.UserName);
        Is2faEnabled = true;

        // First time enabling — issue recovery codes and show them once.
        RecoveryCodes = (await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))?.ToArray();
        RecoveryCodesLeft = RecoveryCodes?.Length ?? 0;
        TempData["Success"] = "Two-factor authentication is now enabled.";
        return Page();
    }

    public async Task<IActionResult> OnPostDisableAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        await _userManager.SetTwoFactorEnabledAsync(user, false);
        await _userManager.ResetAuthenticatorKeyAsync(user);
        await _audit.LogAsync("Disable2FA", "User", user.Id, user.UserName);

        TempData["Success"] = "Two-factor authentication has been disabled.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRegenerateCodesAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        await LoadAsync(user);

        if (!Is2faEnabled)
        {
            TempData["Error"] = "Enable two-factor authentication first.";
            return RedirectToPage();
        }

        RecoveryCodes = (await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))?.ToArray();
        RecoveryCodesLeft = RecoveryCodes?.Length ?? 0;
        await _audit.LogAsync("Regenerate2FARecoveryCodes", "User", user.Id, user.UserName);
        TempData["Success"] = "New recovery codes generated. Save them somewhere safe.";
        return Page();
    }

    private async Task BuildSetupAsync(ApplicationUser user)
    {
        var key = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await _userManager.ResetAuthenticatorKeyAsync(user);
            key = await _userManager.GetAuthenticatorKeyAsync(user);
        }

        ShowSetup = true;
        SharedKey = FormatKey(key!);

        var otpauth = $"otpauth://totp/{_urlEncoder.Encode(Issuer)}:{_urlEncoder.Encode(user.Email ?? user.UserName ?? "user")}" +
                      $"?secret={key}&issuer={_urlEncoder.Encode(Issuer)}&digits=6";

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(otpauth, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(6);
        QrCodeDataUri = "data:image/png;base64," + Convert.ToBase64String(png);
    }

    private static string FormatKey(string key)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
            sb.Append(key.AsSpan(i, Math.Min(4, key.Length - i))).Append(' ');
        return sb.ToString().Trim().ToLowerInvariant();
    }
}
