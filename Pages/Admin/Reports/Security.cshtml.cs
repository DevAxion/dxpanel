using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Pages.Admin.Reports;

public class SecurityModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public SecurityModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public int WafAlerts { get; private set; }
    public int MalwareThreats { get; private set; }
    public int BlockedIps { get; private set; }
    public int FailedLogins { get; private set; }
    public int InfectedFiles { get; private set; }

    public async Task OnGetAsync()
    {
        var since = DateTime.UtcNow.AddDays(-30);
        WafAlerts = await _db.ModSecurityAlerts.CountAsync(a => a.Timestamp >= since);
        MalwareThreats = await _db.MalwareScanResults.CountAsync(m => m.DetectedAt >= since);
        BlockedIps = await _db.BlockedIPs.CountAsync(b => b.BlockedAt >= since);
        FailedLogins = await _db.LoginAttempts.CountAsync(a => !a.Success && a.Timestamp >= since);
        InfectedFiles = await _db.ScanResults.CountAsync(s => s.Status == ScanStatus.Infected && s.ScannedAt >= since);
    }

    public async Task<IActionResult> OnGetExportCsvAsync()
    {
        var since = DateTime.UtcNow.AddDays(-30);
        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,Category,Source,Detail,Action");

        var alerts = await _db.ModSecurityAlerts.Where(a => a.Timestamp >= since).OrderByDescending(a => a.Timestamp).ToListAsync();
        foreach (var a in alerts)
            sb.AppendLine($"{a.Timestamp:s},WAF,{a.IP},{Csv(a.RuleId + " " + a.RuleMessage)},{a.Action}");

        var malware = await _db.MalwareScanResults.Where(m => m.DetectedAt >= since).OrderByDescending(m => m.DetectedAt).ToListAsync();
        foreach (var m in malware)
            sb.AppendLine($"{m.DetectedAt:s},Malware,{Csv(m.FilePath)},{Csv(m.ThreatType + " (" + m.Severity + ")")},{m.Status}");

        var blocks = await _db.BlockedIPs.Where(b => b.BlockedAt >= since).OrderByDescending(b => b.BlockedAt).ToListAsync();
        foreach (var b in blocks)
            sb.AppendLine($"{b.BlockedAt:s},BlockedIP,{b.IP},{Csv(b.Reason)},{(b.IsManual ? "manual" : "auto")}");

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", $"security-report-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private static string Csv(string? v) => string.IsNullOrEmpty(v) ? "" : "\"" + v.Replace("\"", "\"\"") + "\"";
}
