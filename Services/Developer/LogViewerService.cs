using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;

namespace SRXPanel.Services.Developer;

public enum LogKind
{
    PhpError,
    NginxAccess,
    NginxError,
    Application,
    MySqlSlowQuery,
    Cron
}

public enum LogLevelFilter
{
    All,
    Error,
    Warning,
    Notice,
    Info
}

/// <summary>A log the current user is allowed to read. <see cref="Id"/> is opaque and safe to round-trip.</summary>
public record LogSource(string Id, string Name, LogKind Kind, string? DomainName, long SizeBytes, DateTime? Modified)
{
    public bool Clearable => Kind != LogKind.Cron;
}

public record LogLine(string Text, LogLevelFilter Level, DateTime? Timestamp);

public record LogQuery(string? Search, LogLevelFilter Level, DateTime? From, DateTime? To, int Limit = 1000);

public interface ILogViewerService
{
    Task<List<LogSource>> GetSourcesAsync(string userId);
    Task<LogSource?> GetSourceAsync(string userId, string sourceId);

    Task<List<LogLine>> ReadAsync(string userId, string sourceId, LogQuery query);

    /// <summary>The last N lines, used by the live tail.</summary>
    Task<List<LogLine>> TailAsync(string userId, string sourceId, int lines = 100);

    Task<(Stream stream, string fileName)> DownloadAsync(string userId, string sourceId);
    Task ClearAsync(string userId, string sourceId);
}

public class LogViewerService : ILogViewerService
{
    private readonly ApplicationDbContext _db;
    private readonly IFileManagerService _files;
    private readonly IWebHostEnvironment _env;

    public LogViewerService(ApplicationDbContext db, IFileManagerService files, IWebHostEnvironment env)
    {
        _db = db;
        _files = files;
        _env = env;
    }

    /// <summary>Log files live outside the public sandbox so a site can never serve them.</summary>
    private string LogRoot(string userId)
    {
        var root = Path.Combine(_env.ContentRootPath, "App_Data", "homes", userId, "logs");
        Directory.CreateDirectory(root);
        return root;
    }

    public async Task<List<LogSource>> GetSourcesAsync(string userId)
    {
        var domains = await _db.Domains.Where(d => d.UserId == userId).OrderBy(d => d.DomainName).ToListAsync();
        var sources = new List<LogSource>();

        foreach (var domain in domains)
        {
            sources.Add(Describe(userId, $"php:{domain.Id}", $"PHP Error Log", LogKind.PhpError, domain.DomainName));
            sources.Add(Describe(userId, $"access:{domain.Id}", "Nginx Access Log", LogKind.NginxAccess, domain.DomainName));
            sources.Add(Describe(userId, $"error:{domain.Id}", "Nginx Error Log", LogKind.NginxError, domain.DomainName));
        }

        sources.Add(Describe(userId, "app:0", "Application Log (~/logs/app.log)", LogKind.Application, null));
        sources.Add(Describe(userId, "slow:0", "MySQL Slow Query Log", LogKind.MySqlSlowQuery, null));

        var cronRuns = await _db.CronJobLogs.CountAsync(l => l.CronJob!.UserId == userId);
        sources.Add(new LogSource("cron:0", $"Cron Job Log ({cronRuns} runs)", LogKind.Cron, null, 0, null));

        return sources;
    }

    public async Task<LogSource?> GetSourceAsync(string userId, string sourceId)
    {
        var sources = await GetSourcesAsync(userId);
        return sources.FirstOrDefault(s => s.Id == sourceId);
    }

    private LogSource Describe(string userId, string id, string name, LogKind kind, string? domainName)
    {
        var path = PathFor(userId, id, kind, domainName);
        var info = new FileInfo(path);
        return new LogSource(id, name, kind, domainName,
            info.Exists ? info.Length : 0,
            info.Exists ? info.LastWriteTimeUtc : null);
    }

    /// <summary>
    /// Maps an opaque source id to a file. Ids are validated against the user's own
    /// domains before this is called, so no user-controlled path ever reaches the filesystem.
    /// </summary>
    private string PathFor(string userId, string sourceId, LogKind kind, string? domainName)
    {
        var root = LogRoot(userId);
        var slug = string.IsNullOrEmpty(domainName) ? "" : Regex.Replace(domainName, "[^a-zA-Z0-9.-]", "_");

        var fileName = kind switch
        {
            LogKind.PhpError => $"{slug}.php-error.log",
            LogKind.NginxAccess => $"{slug}.access.log",
            LogKind.NginxError => $"{slug}.error.log",
            LogKind.Application => "app.log",
            LogKind.MySqlSlowQuery => "mysql-slow.log",
            _ => "cron.log"
        };
        return Path.Combine(root, fileName);
    }

    private async Task<string> ResolveAsync(string userId, string sourceId)
    {
        var source = await GetSourceAsync(userId, sourceId)
            ?? throw new InvalidOperationException("Log not found.");

        var path = PathFor(userId, sourceId, source.Kind, source.DomainName);

        // Seed a plausible file the first time a log is opened on a host with no real services.
        if (!File.Exists(path))
            await File.WriteAllTextAsync(path, LogSampleGenerator.Generate(source.Kind, source.DomainName));

        return path;
    }

    public async Task<List<LogLine>> ReadAsync(string userId, string sourceId, LogQuery query)
    {
        if (sourceId.StartsWith("cron:")) return await ReadCronAsync(userId, query);

        var path = await ResolveAsync(userId, sourceId);
        var lines = await File.ReadAllLinesAsync(path);

        var results = new List<LogLine>();
        foreach (var raw in lines.Reverse())
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var line = Parse(raw);

            if (query.Level != LogLevelFilter.All && line.Level != query.Level) continue;
            if (!string.IsNullOrWhiteSpace(query.Search) &&
                !raw.Contains(query.Search, StringComparison.OrdinalIgnoreCase)) continue;
            if (query.From is DateTime from && line.Timestamp is DateTime ts && ts < from) continue;
            if (query.To is DateTime to && line.Timestamp is DateTime ts2 && ts2 > to.AddDays(1)) continue;

            results.Add(line);
            if (results.Count >= query.Limit) break;
        }

        results.Reverse();
        return results;
    }

    private async Task<List<LogLine>> ReadCronAsync(string userId, LogQuery query)
    {
        var logs = await _db.CronJobLogs
            .Include(l => l.CronJob)
            .Where(l => l.CronJob!.UserId == userId)
            .OrderByDescending(l => l.StartedAt)
            .Take(query.Limit)
            .ToListAsync();

        return logs
            .Select(l => new LogLine(
                $"[{l.StartedAt:yyyy-MM-dd HH:mm:ss}] {(l.ExitCode == 0 ? "SUCCESS" : "ERROR")} " +
                $"({l.DurationMs}ms, exit {l.ExitCode}) {l.CronJob?.Command} — {FirstLine(l.Output)}",
                l.ExitCode == 0 ? LogLevelFilter.Info : LogLevelFilter.Error,
                l.StartedAt))
            .Where(l => query.Level == LogLevelFilter.All || l.Level == query.Level)
            .Where(l => string.IsNullOrWhiteSpace(query.Search) ||
                        l.Text.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
            .Reverse()
            .ToList();
    }

    private static string FirstLine(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return "(no output)";
        var line = output.Split('\n')[0].Trim();
        return line.Length > 120 ? line[..120] + "…" : line;
    }

    public async Task<List<LogLine>> TailAsync(string userId, string sourceId, int lines = 100) =>
        await ReadAsync(userId, sourceId, new LogQuery(null, LogLevelFilter.All, null, null, lines));

    public async Task<(Stream stream, string fileName)> DownloadAsync(string userId, string sourceId)
    {
        if (sourceId.StartsWith("cron:"))
        {
            var cronLines = await ReadCronAsync(userId, new LogQuery(null, LogLevelFilter.All, null, null, 5000));
            var text = string.Join('\n', cronLines.Select(l => l.Text));
            return (new MemoryStream(Encoding.UTF8.GetBytes(text)), "cron.log");
        }

        var path = await ResolveAsync(userId, sourceId);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return (stream, Path.GetFileName(path));
    }

    public async Task ClearAsync(string userId, string sourceId)
    {
        if (sourceId.StartsWith("cron:"))
        {
            var stale = await _db.CronJobLogs.Where(l => l.CronJob!.UserId == userId).ToListAsync();
            _db.CronJobLogs.RemoveRange(stale);
            await _db.SaveChangesAsync();
            return;
        }

        var path = await ResolveAsync(userId, sourceId);
        // Truncate rather than delete: nginx/php-fpm hold the inode open.
        await File.WriteAllTextAsync(path, "");
    }

    // ---------------- Parsing ----------------

    private static readonly Regex PhpLine = new(@"^\[(?<ts>[^\]]+)\]\s+PHP\s+(?<level>\w+)", RegexOptions.Compiled);
    private static readonly Regex NginxErrorLine = new(@"^(?<ts>\d{4}/\d{2}/\d{2} \d{2}:\d{2}:\d{2})\s+\[(?<level>\w+)\]", RegexOptions.Compiled);
    private static readonly Regex AccessLine = new(@"\[(?<ts>[^\]]+)\]\s+""[A-Z]+[^""]*""\s+(?<status>\d{3})", RegexOptions.Compiled);

    internal static LogLine Parse(string raw)
    {
        var match = PhpLine.Match(raw);
        if (match.Success)
        {
            var level = match.Groups["level"].Value.ToLowerInvariant() switch
            {
                "fatal" or "error" or "parse" => LogLevelFilter.Error,
                "warning" => LogLevelFilter.Warning,
                "notice" or "deprecated" => LogLevelFilter.Notice,
                _ => LogLevelFilter.Info
            };
            return new LogLine(raw, level, ParseDate(match.Groups["ts"].Value));
        }

        match = NginxErrorLine.Match(raw);
        if (match.Success)
        {
            var level = match.Groups["level"].Value.ToLowerInvariant() switch
            {
                "emerg" or "alert" or "crit" or "error" => LogLevelFilter.Error,
                "warn" => LogLevelFilter.Warning,
                "notice" => LogLevelFilter.Notice,
                _ => LogLevelFilter.Info
            };
            return new LogLine(raw, level, ParseDate(match.Groups["ts"].Value.Replace('/', '-')));
        }

        match = AccessLine.Match(raw);
        if (match.Success)
        {
            // In an access log the HTTP status is what makes a line interesting.
            var status = int.Parse(match.Groups["status"].Value);
            var level = status >= 500 ? LogLevelFilter.Error
                : status >= 400 ? LogLevelFilter.Warning
                : LogLevelFilter.Info;
            return new LogLine(raw, level, ParseDate(match.Groups["ts"].Value));
        }

        var lowered = raw.ToLowerInvariant();
        var fallback = lowered.Contains("error") || lowered.Contains("fatal") ? LogLevelFilter.Error
            : lowered.Contains("warn") ? LogLevelFilter.Warning
            : lowered.Contains("notice") ? LogLevelFilter.Notice
            : LogLevelFilter.Info;

        return new LogLine(raw, fallback, null);
    }

    private static DateTime? ParseDate(string value)
    {
        value = value.Trim();

        // "10/Jul/2026:09:15:42 +0000" (access) or "10-Jul-2026 09:15:42 UTC" (php)
        var formats = new[]
        {
            "dd/MMM/yyyy:HH:mm:ss zzz", "dd-MMM-yyyy HH:mm:ss UTC", "dd-MMM-yyyy HH:mm:ss",
            "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss"
        };

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AllowWhiteSpaces, out var parsed))
                return parsed;
        }

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var loose)
            ? loose
            : null;
    }
}
