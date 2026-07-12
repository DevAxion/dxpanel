using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Security;

public record ScanSummary(int Scanned, int Infected, List<ScanResult> Results);

public interface IClamAvService
{
    Task<ScanResult> ScanFileAsync(string userId, string path);
    Task<ScanSummary> ScanDirectoryAsync(string userId, string path);
    Task<QuarantinedFile> QuarantineFileAsync(string userId, string path, string? threat);
    Task RestoreFileAsync(string userId, int quarantineId);
    Task DeleteQuarantinedAsync(string userId, int quarantineId);
    Task<List<QuarantinedFile>> GetQuarantineAsync(string userId);
    Task<DateTime> UpdateDefinitionsAsync();
    Task<DateTime> GetDefinitionDateAsync();
    Task<List<ScanResult>> GetRecentScansAsync(string userId, int take = 50);
}

/// <summary>
/// ClamAV wrapper. Simulation-safe: on dev it produces deterministic mock scan
/// results (occasionally flagging an EICAR-style test signature) and records them.
/// </summary>
public class ClamAvService : IClamAvService
{
    private const string ServiceName = "clamav";
    private readonly ApplicationDbContext _db;
    private readonly ICommandRunner _runner;
    private readonly ISecurityBroadcast _broadcast;

    private static readonly string[] SampleTree =
    {
        "public_html/index.php", "public_html/wp-config.php", "public_html/wp-content/uploads/2026/logo.png",
        "public_html/wp-content/uploads/2026/invoice.pdf", "public_html/wp-content/plugins/cache/x.php",
        "public_html/.htaccess", "public_html/css/style.css", "public_html/js/app.js", "tmp/session_a1b2",
        "public_html/wp-content/uploads/shell.php.suspected"
    };

    public ClamAvService(ApplicationDbContext db, ICommandRunner runner, ISecurityBroadcast broadcast)
    {
        _db = db;
        _runner = runner;
        _broadcast = broadcast;
    }

    public async Task<ScanResult> ScanFileAsync(string userId, string path)
    {
        var cmd = await _runner.RunAsync($"clamdscan --fdpass {path}", ServiceName);
        // Deterministic pseudo-detection: files hinting at a webshell are flagged.
        var infected = path.Contains("shell", StringComparison.OrdinalIgnoreCase)
                       || path.EndsWith(".suspected", StringComparison.OrdinalIgnoreCase);
        var result = new ScanResult
        {
            UserId = userId, Path = path,
            Status = infected ? ScanStatus.Infected : ScanStatus.Clean,
            ThreatName = infected ? "PHP.Webshell.Agent" : null,
            Action = ScanAction.None, ScannedAt = DateTime.UtcNow
        };
        _db.ScanResults.Add(result);
        await _db.SaveChangesAsync();
        return result;
    }

    public async Task<ScanSummary> ScanDirectoryAsync(string userId, string path)
    {
        await _runner.RunAsync($"clamdscan -r --fdpass {path}", ServiceName);
        var results = new List<ScanResult>();
        var total = SampleTree.Length;
        for (var i = 0; i < total; i++)
        {
            var file = $"{path.TrimEnd('/')}/{SampleTree[i]}";
            var infected = SampleTree[i].Contains("shell") || SampleTree[i].EndsWith(".suspected");
            var r = new ScanResult
            {
                UserId = userId, Path = file,
                Status = infected ? ScanStatus.Infected : ScanStatus.Clean,
                ThreatName = infected ? "PHP.Webshell.Agent" : null,
                Action = ScanAction.None, ScannedAt = DateTime.UtcNow
            };
            results.Add(r);
            await _broadcast.ScanProgressAsync(userId, (int)(100.0 * (i + 1) / total), SampleTree[i]);
        }
        _db.ScanResults.AddRange(results);
        await _db.SaveChangesAsync();
        var infectedCount = results.Count(r => r.Status == ScanStatus.Infected);
        await _broadcast.ScanCompleteAsync(userId, total, infectedCount);
        return new ScanSummary(total, infectedCount, results);
    }

    public async Task<QuarantinedFile> QuarantineFileAsync(string userId, string path, string? threat)
    {
        var qpath = $"/var/quarantine/{userId}/{Guid.NewGuid():N}";
        await _runner.RunAsync($"mv {path} {qpath}", ServiceName);
        var q = new QuarantinedFile { UserId = userId, OriginalPath = path, QuarantinePath = qpath, ThreatName = threat, QuarantinedAt = DateTime.UtcNow };
        _db.QuarantinedFiles.Add(q);

        var scan = await _db.ScanResults.Where(s => s.UserId == userId && s.Path == path)
            .OrderByDescending(s => s.ScannedAt).FirstOrDefaultAsync();
        if (scan != null) scan.Action = ScanAction.Quarantined;
        await _db.SaveChangesAsync();
        return q;
    }

    public async Task RestoreFileAsync(string userId, int quarantineId)
    {
        var q = await _db.QuarantinedFiles.FirstOrDefaultAsync(x => x.Id == quarantineId && x.UserId == userId);
        if (q == null || q.IsDeleted) return;
        await _runner.RunAsync($"mv {q.QuarantinePath} {q.OriginalPath}", ServiceName);
        q.RestoredAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteQuarantinedAsync(string userId, int quarantineId)
    {
        var q = await _db.QuarantinedFiles.FirstOrDefaultAsync(x => x.Id == quarantineId && x.UserId == userId);
        if (q == null) return;
        await _runner.RunAsync($"rm -f {q.QuarantinePath}", ServiceName);
        q.IsDeleted = true;
        await _db.SaveChangesAsync();
    }

    public Task<List<QuarantinedFile>> GetQuarantineAsync(string userId) =>
        _db.QuarantinedFiles.Where(q => q.UserId == userId && !q.IsDeleted && q.RestoredAt == null)
            .OrderByDescending(q => q.QuarantinedAt).ToListAsync();

    public async Task<DateTime> UpdateDefinitionsAsync()
    {
        await _runner.RunAsync("freshclam", ServiceName);
        var settings = await _db.SecuritySettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (settings != null)
        {
            settings.ClamAvDefinitionsDate = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        return DateTime.UtcNow;
    }

    public async Task<DateTime> GetDefinitionDateAsync() =>
        (await _db.SecuritySettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1))?.ClamAvDefinitionsDate
            ?? DateTime.UtcNow.AddDays(-1);

    public Task<List<ScanResult>> GetRecentScansAsync(string userId, int take = 50) =>
        _db.ScanResults.Where(s => s.UserId == userId).OrderByDescending(s => s.ScannedAt).Take(take).ToListAsync();
}
