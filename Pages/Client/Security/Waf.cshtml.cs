using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Security;

namespace SRXPanel.Pages.Client.Security;

public class WafModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IModSecurityService _waf;

    public WafModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IModSecurityService waf)
    {
        _db = db;
        _userManager = userManager;
        _waf = waf;
    }

    [BindProperty(SupportsGet = true)] public int? DomainId { get; set; }

    public List<Domain> Domains { get; private set; } = new();
    public Domain? Selected { get; private set; }
    public WafStatus? Status { get; private set; }
    public List<ModSecurityAlert> Alerts { get; private set; } = new();
    public List<WafCustomRule> Rules { get; private set; } = new();
    public List<WafIpRule> IpRules { get; private set; } = new();

    private async Task<bool> LoadAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return false;
        Domains = await _db.Domains.Where(d => d.UserId == user.Id).OrderBy(d => d.DomainName).ToListAsync();
        if (Domains.Count == 0) return true;

        Selected = DomainId.HasValue ? Domains.FirstOrDefault(d => d.Id == DomainId) : Domains.First();
        if (Selected == null) return true;

        Status = await _waf.GetStatusAsync(Selected.Id);
        Alerts = await _waf.GetAlertsAsync(Selected.Id, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow);
        Rules = await _waf.GetCustomRulesAsync(Selected.Id);
        IpRules = await _waf.GetIpRulesAsync(Selected.Id);
        return true;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync()) return Challenge();
        return Page();
    }

    private async Task<int?> OwnedDomainAsync(int domainId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return null;
        return await _db.Domains.AnyAsync(d => d.Id == domainId && d.UserId == user.Id) ? domainId : null;
    }

    public async Task<IActionResult> OnPostToggleAsync(int domainId, bool enable)
    {
        if (await OwnedDomainAsync(domainId) is not int id) return Forbid();
        if (enable) await _waf.EnableAsync(id); else await _waf.DisableAsync(id);
        TempData["Success"] = enable ? "WAF enabled for this domain." : "WAF disabled.";
        return RedirectToPage(new { domainId });
    }

    public async Task<IActionResult> OnPostSetModeAsync(int domainId, WafMode mode)
    {
        if (await OwnedDomainAsync(domainId) is not int id) return Forbid();
        await _waf.SetModeAsync(id, mode);
        TempData["Success"] = $"WAF mode set to {mode}.";
        return RedirectToPage(new { domainId });
    }

    public async Task<IActionResult> OnPostAddRuleAsync(int domainId, string ruleText, string? description)
    {
        if (await OwnedDomainAsync(domainId) is not int id) return Forbid();
        if (!string.IsNullOrWhiteSpace(ruleText)) await _waf.AddCustomRuleAsync(id, ruleText, description);
        return RedirectToPage(new { domainId });
    }

    public async Task<IActionResult> OnPostRemoveRuleAsync(int domainId, int ruleId)
    {
        if (await OwnedDomainAsync(domainId) is not int id) return Forbid();
        await _waf.RemoveCustomRuleAsync(id, ruleId);
        return RedirectToPage(new { domainId });
    }

    public async Task<IActionResult> OnPostAddIpAsync(int domainId, string ip, WafIpAction action)
    {
        if (await OwnedDomainAsync(domainId) is not int id) return Forbid();
        if (!string.IsNullOrWhiteSpace(ip)) await _waf.AddIpRuleAsync(id, ip, action);
        return RedirectToPage(new { domainId });
    }

    public async Task<IActionResult> OnPostRemoveIpAsync(int domainId, int ruleId)
    {
        if (await OwnedDomainAsync(domainId) is not int _) return Forbid();
        await _waf.RemoveIpRuleAsync(ruleId);
        return RedirectToPage(new { domainId });
    }
}
