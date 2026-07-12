using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Portal;

namespace SRXPanel.Pages.Client;

public class SettingsModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IApiKeyService _apiKeys;
    private readonly IAuditLogService _audit;
    private readonly UrlEncoder _urlEncoder;

    public SettingsModel(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager,
        IApiKeyService apiKeys, IAuditLogService audit, UrlEncoder urlEncoder)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _apiKeys = apiKeys;
        _audit = audit;
        _urlEncoder = urlEncoder;
    }

    public ApplicationUser CurrentUser { get; set; } = null!;
    public bool TwoFactorEnabled { get; set; }
    public string? AuthenticatorUri { get; set; }
    public string? SharedKey { get; set; }
    public List<ApiKey> Keys { get; set; } = new();

    [TempData] public string? NewApiKey { get; set; }
    [TempData] public string? RecoveryCodes { get; set; }

    public static readonly string[] TimeZones = { "UTC", "Europe/London", "Europe/Berlin", "Asia/Baku", "America/New_York", "America/Los_Angeles" };
    public static readonly (string Code, string Name)[] Languages = { ("en", "English"), ("az", "Azərbaycan"), ("de", "Deutsch"), ("tr", "Türkçe") };

    private async Task<ApplicationUser?> LoadAsync(bool setup2fa = false)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return null;
        CurrentUser = user;
        TwoFactorEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
        Keys = await _apiKeys.ListAsync(user.Id);

        if (setup2fa && !TwoFactorEnabled)
        {
            var key = await _userManager.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrEmpty(key))
            {
                await _userManager.ResetAuthenticatorKeyAsync(user);
                key = await _userManager.GetAuthenticatorKeyAsync(user);
            }
            SharedKey = FormatKey(key!);
            var email = await _userManager.GetEmailAsync(user);
            AuthenticatorUri = $"otpauth://totp/{_urlEncoder.Encode("SRXPanel")}:{_urlEncoder.Encode(email!)}" +
                               $"?secret={key}&issuer={_urlEncoder.Encode("SRXPanel")}&digits=6";
        }
        return user;
    }

    public async Task<IActionResult> OnGetAsync(bool setup = false)
    {
        if (await LoadAsync(setup) == null) return Challenge();
        return Page();
    }

    public async Task<IActionResult> OnPostProfileAsync(string fullName, string? phone, string timeZone, string language)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        user.FullName = fullName ?? user.FullName;
        user.PhoneNumber = phone;
        user.TimeZone = TimeZones.Contains(timeZone) ? timeZone : user.TimeZone;
        user.Language = Languages.Any(l => l.Code == language) ? language : user.Language;
        await _userManager.UpdateAsync(user);
        await _audit.LogAsync("Update", "Profile", user.Id, "profile updated");
        TempData["Success"] = "Profile updated.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPasswordAsync(string currentPassword, string newPassword, string confirmPassword)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        if (newPassword != confirmPassword) { TempData["Error"] = "Passwords do not match."; return RedirectToPage(); }
        var result = await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
        {
            TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
            return RedirectToPage();
        }
        await _signInManager.RefreshSignInAsync(user);
        await _audit.LogAsync("ChangePassword", "User", user.Id, "password changed");
        TempData["Success"] = "Password changed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEnable2faAsync(string verificationCode)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var code = (verificationCode ?? "").Replace(" ", "").Replace("-", "");
        var valid = await _userManager.VerifyTwoFactorTokenAsync(user, _userManager.Options.Tokens.AuthenticatorTokenProvider, code);
        if (!valid)
        {
            TempData["Error"] = "Invalid verification code. Try again.";
            return RedirectToPage(new { setup = true });
        }
        await _userManager.SetTwoFactorEnabledAsync(user, true);
        var codes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        RecoveryCodes = string.Join(" ", codes ?? Enumerable.Empty<string>());
        await _audit.LogAsync("Enable2FA", "User", user.Id, "two-factor enabled");
        TempData["Success"] = "Two-factor authentication enabled.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDisable2faAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        await _userManager.SetTwoFactorEnabledAsync(user, false);
        await _userManager.ResetAuthenticatorKeyAsync(user);
        await _audit.LogAsync("Disable2FA", "User", user.Id, "two-factor disabled");
        TempData["Success"] = "Two-factor authentication disabled.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostGenerateApiKeyAsync(string name)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var (_, plaintext) = await _apiKeys.GenerateAsync(user.Id, string.IsNullOrWhiteSpace(name) ? "API Key" : name.Trim());
        NewApiKey = plaintext;
        await _audit.LogAsync("Create", "ApiKey", null, name);
        TempData["Success"] = "API key generated. Copy it now — it won't be shown again.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRevokeApiKeyAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        await _apiKeys.RevokeAsync(user.Id, id);
        TempData["Success"] = "API key revoked.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostNotificationsAsync(bool invoices, bool ssl, bool disk, bool support)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        user.NotifyInvoices = invoices;
        user.NotifySslExpiry = ssl;
        user.NotifyDiskUsage = disk;
        user.NotifySupport = support;
        await _userManager.UpdateAsync(user);
        TempData["Success"] = "Notification preferences saved.";
        return RedirectToPage();
    }

    private static string FormatKey(string unformattedKey)
    {
        var result = new StringBuilder();
        int i = 0;
        while (i + 4 < unformattedKey.Length)
        {
            result.Append(unformattedKey.AsSpan(i, 4)).Append(' ');
            i += 4;
        }
        if (i < unformattedKey.Length) result.Append(unformattedKey.AsSpan(i));
        return result.ToString().ToLowerInvariant();
    }
}
