using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services.Security;

namespace SRXPanel.Pages.Admin.Security;

public class IpManagerModel : PageModel
{
    private readonly IIpManagerService _ip;

    public IpManagerModel(IIpManagerService ip)
    {
        _ip = ip;
    }

    public List<IpAccessRule> Whitelist { get; private set; } = new();
    public List<IpAccessRule> Blacklist { get; private set; } = new();
    public List<IpAccessRule> Countries { get; private set; } = new();
    public string FirewallStatus { get; private set; } = "";
    public int RateLimit { get; private set; }

    public async Task OnGetAsync()
    {
        Whitelist = await _ip.GetRulesAsync(IpRuleKind.WhitelistIp);
        Blacklist = await _ip.GetRulesAsync(IpRuleKind.BlacklistIp);
        Countries = await _ip.GetRulesAsync(IpRuleKind.BlockCountry);
        FirewallStatus = await _ip.GetFirewallStatusAsync();
        RateLimit = await _ip.GetRateLimitAsync();
    }

    public async Task<IActionResult> OnPostAddAsync(IpRuleKind kind, string value, string? reason)
    {
        if (!string.IsNullOrWhiteSpace(value)) await _ip.AddRuleAsync(kind, value, reason);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(int id)
    {
        await _ip.RemoveRuleAsync(id);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRateLimitAsync(int perMinute)
    {
        await _ip.SetRateLimitAsync(perMinute);
        TempData["Success"] = "Rate limit updated.";
        return RedirectToPage();
    }
}
