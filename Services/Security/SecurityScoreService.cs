using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Services.Security;

public record ScoreItem(string Label, bool Achieved, int Points, string Recommendation);
public record SecurityScore(int Total, List<ScoreItem> Items)
{
    public IEnumerable<ScoreItem> Recommendations => Items.Where(i => !i.Achieved);
    public string Grade => Total >= 90 ? "A" : Total >= 75 ? "B" : Total >= 60 ? "C" : Total >= 40 ? "D" : "F";
}

public record SecurityEvent(DateTime At, string Type, string Message, string Severity);

public interface ISecurityScoreService
{
    Task<SecurityScore> GetScoreAsync(ApplicationUser user);
    Task<List<SecurityEvent>> GetTimelineAsync(string userId, int take = 20);
}

public class SecurityScoreService : ISecurityScoreService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public SecurityScoreService(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public async Task<SecurityScore> GetScoreAsync(ApplicationUser user)
    {
        var domainIds = await _db.Domains.Where(d => d.UserId == user.Id).Select(d => d.Id).ToListAsync();

        var wafOn = domainIds.Count > 0 && await _db.WafConfigs.AnyAsync(w => domainIds.Contains(w.DomainId) && w.Enabled);
        var emailOk = domainIds.Count > 0 && await _db.EmailSecurities.AnyAsync(e => domainIds.Contains(e.DomainId) && e.DkimValid && e.SpfValid && e.DmarcValid);
        var sslOn = await _db.Domains.AnyAsync(d => d.UserId == user.Id && d.SslEnabled);
        var noMalware = !await _db.MalwareScanResults.AnyAsync(m => m.UserId == user.Id && m.Status == MalwareStatus.Detected);
        var twoFa = user.TwoFactorEnabled;
        // Password policy is enforced at registration (upper/lower/digit/symbol, 8+), so credit it.
        var strongPw = true;

        var items = new List<ScoreItem>
        {
            new("Web Application Firewall (WAF) enabled", wafOn, 20, "Enable ModSecurity for your domains under Security → WAF."),
            new("Email authentication (DKIM/SPF/DMARC)", emailOk, 20, "Configure and verify DKIM, SPF and DMARC under Email Security."),
            new("SSL certificates active", sslOn, 20, "Enable free SSL on your domains under SSL."),
            new("No malware detected", noMalware, 20, "Resolve detected threats under Security → Malware."),
            new("Strong password policy", strongPw, 10, "Use a long, unique password."),
            new("Two-factor authentication (2FA)", twoFa, 10, "Enable 2FA in your account settings.")
        };

        return new SecurityScore(items.Where(i => i.Achieved).Sum(i => i.Points), items);
    }

    public async Task<List<SecurityEvent>> GetTimelineAsync(string userId, int take = 20)
    {
        var domainIds = await _db.Domains.Where(d => d.UserId == userId).Select(d => d.Id).ToListAsync();
        var events = new List<SecurityEvent>();

        var malware = await _db.MalwareScanResults.Where(m => m.UserId == userId)
            .OrderByDescending(m => m.DetectedAt).Take(take).ToListAsync();
        events.AddRange(malware.Select(m => new SecurityEvent(m.DetectedAt, "Malware",
            $"{m.Severity} — {m.ThreatType}: {m.FilePath}", m.Severity.ToString())));

        var scans = await _db.ScanResults.Where(s => s.UserId == userId && s.Status == ScanStatus.Infected)
            .OrderByDescending(s => s.ScannedAt).Take(take).ToListAsync();
        events.AddRange(scans.Select(s => new SecurityEvent(s.ScannedAt, "Antivirus",
            $"Infected: {s.ThreatName} in {s.Path}", "High")));

        if (domainIds.Count > 0)
        {
            var alerts = await _db.ModSecurityAlerts.Where(a => domainIds.Contains(a.DomainId))
                .OrderByDescending(a => a.Timestamp).Take(take).ToListAsync();
            events.AddRange(alerts.Select(a => new SecurityEvent(a.Timestamp, "WAF",
                $"{a.Action} {a.Method} {a.URI} from {a.IP} (rule {a.RuleId})", "Medium")));
        }

        return events.OrderByDescending(e => e.At).Take(take).ToList();
    }
}
