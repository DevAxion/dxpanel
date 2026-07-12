using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Cloudflare;

namespace SRXPanel.Pages.Client.Cloudflare;

public class ConnectModel : PageModel
{
    private readonly ICloudflareManager _cf;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _auditLog;

    public ConnectModel(ICloudflareManager cf, UserManager<ApplicationUser> userManager, IAuditLogService auditLog)
    {
        _cf = cf;
        _userManager = userManager;
        _auditLog = auditLog;
    }

    public CloudflareAccount? Account { get; private set; }
    public List<CfZone> RemoteZones { get; private set; } = new();
    public List<Domain> Linkable { get; private set; } = new();
    public List<CloudflareDomain> Linked { get; private set; } = new();

    /// <summary>1 = token, 2 = link zones.</summary>
    public int Step { get; private set; } = 1;

    private async Task<string?> UserIdAsync() => (await _userManager.GetUserAsync(User))?.Id;

    public async Task<IActionResult> OnGetAsync()
    {
        var userId = await UserIdAsync();
        if (userId == null) return Challenge();

        await LoadAsync(userId);
        Step = Account != null ? 2 : 1;
        return Page();
    }

    private async Task LoadAsync(string userId)
    {
        Account = await _cf.GetAccountAsync(userId);
        if (Account != null)
        {
            RemoteZones = await _cf.ListRemoteZonesAsync(userId);
            Linkable = await _cf.GetLinkableDomainsAsync(userId);
            Linked = await _cf.GetLinkedDomainsAsync(userId);
        }
    }

    /// <summary>Tests a token without saving — used by the "Test token" button.</summary>
    public async Task<IActionResult> OnPostTestAsync(string apiToken)
    {
        var result = await _cf.Gateway.ValidateTokenAsync(apiToken ?? "");
        return new JsonResult(new
        {
            valid = result.Valid,
            account = result.AccountName ?? result.AccountId,
            scopes = result.Scopes,
            error = result.Error
        });
    }

    public async Task<IActionResult> OnPostConnectAsync(string apiToken, string? email)
    {
        var userId = await UserIdAsync();
        if (userId == null) return Challenge();

        var result = await _cf.ConnectAsync(userId, apiToken, email);
        if (!result.Valid)
        {
            TempData["Error"] = result.Error ?? "Cloudflare rejected the token.";
            return RedirectToPage();
        }

        await _auditLog.LogAsync("Connect", "CloudflareAccount", userId, result.AccountName ?? "");
        TempData["Success"] = "Cloudflare connected. Now link your domains to their zones.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostLinkAsync(int domainId, string? zoneId)
    {
        var userId = await UserIdAsync();
        if (userId == null) return Challenge();

        try
        {
            var cf = await _cf.LinkDomainAsync(userId, domainId, zoneId);
            await _auditLog.LogAsync("Link", "CloudflareDomain", cf.Id.ToString(), cf.Domain?.DomainName ?? "");
            TempData["Success"] = $"Linked. Next: review DNS before syncing.";
            return RedirectToPage("/Client/Cloudflare/Migrate", new { domainId });
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToPage();
        }
    }
}
