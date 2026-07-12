using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Developer;

namespace SRXPanel.Pages.Client.Developer;

public class SshModel : PageModel
{
    private readonly ISshKeyService _ssh;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _auditLog;
    private readonly PanelSettings _panel;

    public SshModel(ISshKeyService ssh, UserManager<ApplicationUser> userManager,
        IAuditLogService auditLog, IOptionsMonitor<PanelSettings> panel)
    {
        _ssh = ssh;
        _userManager = userManager;
        _auditLog = auditLog;
        _panel = panel.CurrentValue;
    }

    public List<SshKey> Keys { get; private set; } = new();
    public SshAccess Access { get; private set; } = null!;
    public List<SshAccessLog> AccessLog { get; private set; } = new();

    public string Host => _panel.Hostname;
    public string Username { get; private set; } = "";

    /// <summary>Shown exactly once, right after a key pair is generated.</summary>
    public string? GeneratedPrivateKey { get; private set; }
    public string? GeneratedFingerprint { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        await LoadAsync(user);

        GeneratedPrivateKey = TempData["GeneratedPrivateKey"] as string;
        GeneratedFingerprint = TempData["GeneratedFingerprint"] as string;

        return Page();
    }

    private async Task LoadAsync(ApplicationUser user)
    {
        Username = user.UserName ?? "";
        Keys = await _ssh.GetKeysAsync(user.Id);
        Access = await _ssh.GetAccessAsync(user.Id);
        AccessLog = await _ssh.GetAccessLogAsync(user.Id, 10);
    }

    public async Task<IActionResult> OnPostAddKeyAsync(string publicKey, string label)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        try
        {
            var key = await _ssh.AddKeyAsync(user.Id, publicKey, label);
            await _auditLog.LogAsync("Create", "SshKey", key.Id.ToString(), key.Label);
            TempData["Success"] = $"Key '{key.Label}' added — {key.Fingerprint}";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveKeyAsync(int keyId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        await _ssh.RemoveKeyAsync(user.Id, keyId);
        await _auditLog.LogAsync("Delete", "SshKey", keyId.ToString(), "");
        TempData["Success"] = "Key removed. It can no longer be used to sign in.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostGenerateAsync(string label, int bits)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        try
        {
            var pair = await _ssh.GenerateKeyPairAsync(user.Id, label, bits);
            await _auditLog.LogAsync("Create", "SshKey", pair.Fingerprint, $"generated RSA-{bits}");

            // The private key is never stored — hand it over once via TempData.
            TempData["GeneratedPrivateKey"] = pair.PrivateKeyPem;
            TempData["GeneratedFingerprint"] = pair.Fingerprint;
            TempData["Success"] = "Key pair generated. Download the private key now — it is not stored anywhere.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAccessAsync(bool enable)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var result = enable
            ? await _ssh.EnableSshAccessAsync(user.Id)
            : await _ssh.DisableSshAccessAsync(user.Id);

        await _auditLog.LogAsync(enable ? "Enable" : "Disable", "SshAccess", user.Id, user.UserName ?? "");
        TempData["Success"] = result.Message + (result.Simulated ? " (simulated)" : "");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPasswordAuthAsync(bool allow)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        await _ssh.SetPasswordAuthAsync(user.Id, allow);
        TempData["Success"] = allow
            ? "Password authentication enabled. Key-based auth is safer — consider turning this back off."
            : "Password authentication disabled. Only your registered keys can sign in.";
        return RedirectToPage();
    }
}
