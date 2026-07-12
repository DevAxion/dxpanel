using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Security;

namespace SRXPanel.Pages.Admin.Security;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IClamAvService _av;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISecurityScoreService _score;

    public IndexModel(ApplicationDbContext db, IClamAvService av, UserManager<ApplicationUser> userManager, ISecurityScoreService score)
    {
        _db = db;
        _av = av;
        _userManager = userManager;
        _score = score;
    }

    public int AlertsToday { get; private set; }
    public int AlertsWeek { get; private set; }
    public int AlertsMonth { get; private set; }
    public int BlockedIps { get; private set; }
    public int ActiveThreats { get; private set; }
    public int WafProtectedDomains { get; private set; }
    public DateTime ClamAvDate { get; private set; }

    public List<(string Rule, string Message, int Count)> TopThreats { get; private set; } = new();
    public List<(string User, int Score)> LowScoreUsers { get; private set; } = new();
    public List<MalwareScanResult> CriticalEvents { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var now = DateTime.UtcNow;
        AlertsToday = await _db.ModSecurityAlerts.CountAsync(a => a.Timestamp >= now.Date);
        AlertsWeek = await _db.ModSecurityAlerts.CountAsync(a => a.Timestamp >= now.AddDays(-7));
        AlertsMonth = await _db.ModSecurityAlerts.CountAsync(a => a.Timestamp >= now.AddDays(-30));
        BlockedIps = await _db.BlockedIPs.CountAsync(b => b.UnblockedAt == null && (b.ExpiresAt == null || b.ExpiresAt > now));
        ActiveThreats = await _db.MalwareScanResults.CountAsync(m => m.Status == MalwareStatus.Detected);
        WafProtectedDomains = await _db.WafConfigs.CountAsync(w => w.Enabled);
        ClamAvDate = await _av.GetDefinitionDateAsync();

        TopThreats = (await _db.ModSecurityAlerts.GroupBy(a => new { a.RuleId, a.RuleMessage })
                .Select(g => new { g.Key.RuleId, g.Key.RuleMessage, Count = g.Count() })
                .OrderByDescending(x => x.Count).Take(5).ToListAsync())
            .Select(x => (x.RuleId, x.RuleMessage, x.Count)).ToList();

        CriticalEvents = await _db.MalwareScanResults
            .Where(m => m.Severity == MalwareSeverity.Critical || m.Severity == MalwareSeverity.High)
            .OrderByDescending(m => m.DetectedAt).Take(10).ToListAsync();

        // Compute scores for client users (cap the number scanned for performance).
        var clients = await _userManager.GetUsersInRoleAsync(Roles.Client);
        foreach (var u in clients.Take(25))
        {
            var s = await _score.GetScoreAsync(u);
            if (s.Total < 60) LowScoreUsers.Add((u.UserName ?? u.Email ?? u.Id, s.Total));
        }
        LowScoreUsers = LowScoreUsers.OrderBy(x => x.Score).Take(10).ToList();
    }
}
