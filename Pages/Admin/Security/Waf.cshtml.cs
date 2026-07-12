using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Security;

namespace SRXPanel.Pages.Admin.Security;

public class WafModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IModSecurityService _waf;

    public WafModel(ApplicationDbContext db, IModSecurityService waf)
    {
        _db = db;
        _waf = waf;
    }

    public int TotalAlerts { get; private set; }
    public string CrsVersion { get; private set; } = "";
    public List<(string IP, int Count)> TopIps { get; private set; } = new();
    public List<(string Rule, string Message, int Count)> TopRules { get; private set; } = new();
    public List<(string Domain, bool Enabled, WafMode Mode, int Alerts)> DomainStatus { get; private set; } = new();

    public async Task OnGetAsync()
    {
        TotalAlerts = await _db.ModSecurityAlerts.CountAsync();
        CrsVersion = (await _db.SecuritySettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1))?.CrsVersion ?? "4.3.0";

        TopIps = (await _db.ModSecurityAlerts.GroupBy(a => a.IP)
                .Select(g => new { g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(8).ToListAsync())
            .Select(x => (x.Key, x.Count)).ToList();

        TopRules = (await _db.ModSecurityAlerts.GroupBy(a => new { a.RuleId, a.RuleMessage })
                .Select(g => new { g.Key.RuleId, g.Key.RuleMessage, Count = g.Count() }).OrderByDescending(x => x.Count).Take(8).ToListAsync())
            .Select(x => (x.RuleId, x.RuleMessage, x.Count)).ToList();

        var configs = await _db.WafConfigs.Include(c => c.Domain).ToListAsync();
        var alertCounts = await _db.ModSecurityAlerts.GroupBy(a => a.DomainId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        DomainStatus = configs.Select(c => (c.Domain?.DomainName ?? $"#{c.DomainId}", c.Enabled, c.Mode, alertCounts.GetValueOrDefault(c.DomainId))).ToList();
    }

    public async Task<IActionResult> OnPostUpdateCrsAsync()
    {
        var v = await _waf.UpdateCrsAsync();
        TempData["Success"] = $"OWASP Core Rule Set updated to v{v}.";
        return RedirectToPage();
    }
}
