using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Security;

namespace SRXPanel.Pages.Client.Security;

public class BlacklistModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IModSecurityService _waf;

    public BlacklistModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IModSecurityService waf)
    {
        _db = db;
        _userManager = userManager;
        _waf = waf;
    }

    [BindProperty(SupportsGet = true)] public int? DomainId { get; set; }

    public List<Domain> Domains { get; private set; } = new();
    public Domain? Selected { get; private set; }
    public List<WafIpRule> Blocked { get; private set; } = new();
    public List<WafIpRule> Allowed { get; private set; } = new();

    private async Task<bool> LoadAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return false;
        Domains = await _db.Domains.Where(d => d.UserId == user.Id).OrderBy(d => d.DomainName).ToListAsync();
        if (Domains.Count == 0) return true;

        Selected = DomainId.HasValue ? Domains.FirstOrDefault(d => d.Id == DomainId) : Domains.First();
        if (Selected == null) return true;

        var rules = await _waf.GetIpRulesAsync(Selected.Id);
        Blocked = rules.Where(r => r.Action == WafIpAction.Block).ToList();
        Allowed = rules.Where(r => r.Action == WafIpAction.Whitelist).ToList();
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

    public async Task<IActionResult> OnPostAddAsync(int domainId, string ip, WafIpAction action)
    {
        if (await OwnedDomainAsync(domainId) is not int id) return Forbid();
        if (!string.IsNullOrWhiteSpace(ip))
        {
            await _waf.AddIpRuleAsync(id, ip.Trim(), action);
            TempData["Success"] = $"{ip.Trim()} added to the {(action == WafIpAction.Block ? "blacklist" : "whitelist")}.";
        }
        return RedirectToPage(new { domainId });
    }

    public async Task<IActionResult> OnPostRemoveAsync(int domainId, int ruleId)
    {
        if (await OwnedDomainAsync(domainId) is not int _) return Forbid();
        await _waf.RemoveIpRuleAsync(ruleId);
        TempData["Success"] = "IP rule removed.";
        return RedirectToPage(new { domainId });
    }
}
