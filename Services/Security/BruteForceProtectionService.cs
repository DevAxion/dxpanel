using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Security;

public record AttackReport(int TotalAttempts, int FailedAttempts, int BlockedIps, List<TopIp> TopIps, List<TopCountry> TopCountries);
public record TopIp(string IP, int Count, string? Country);
public record TopCountry(string Country, int Count);

public interface IBruteForceService
{
    Task RecordAttemptAsync(string ip, LoginAttemptType type, string? username, bool success, string? userAgent = null);
    Task<bool> IsBlockedAsync(string ip);
    Task BlockIpAsync(string ip, TimeSpan? duration, string reason, bool manual);
    Task UnblockIpAsync(int blockedId);
    Task<List<BlockedIP>> GetBlockedIpsAsync();
    Task<int> GetFailedAttemptsAsync(string ip, int hours);
    Task<AttackReport> GetAttackReportAsync(DateTime from, DateTime to);
    Task<List<LoginAttempt>> GetRecentAttemptsAsync(int take = 50);
}

/// <summary>
/// Records login attempts (panel/FTP/SMTP/SSH), auto-blocks IPs that exceed the
/// configured threshold and drives the admin real-time feed. Simulation-safe:
/// firewall changes go through ICommandRunner.
/// </summary>
public class BruteForceProtectionService : IBruteForceService
{
    private const string ServiceName = "fail2ban";
    private readonly ApplicationDbContext _db;
    private readonly ICommandRunner _runner;
    private readonly ISecurityBroadcast _broadcast;

    public BruteForceProtectionService(ApplicationDbContext db, ICommandRunner runner, ISecurityBroadcast broadcast)
    {
        _db = db;
        _runner = runner;
        _broadcast = broadcast;
    }

    public async Task RecordAttemptAsync(string ip, LoginAttemptType type, string? username, bool success, string? userAgent = null)
    {
        var attempt = new LoginAttempt
        {
            IP = ip, Type = type, Username = username, Success = success, UserAgent = userAgent,
            Country = GeoLookup(ip), Timestamp = DateTime.UtcNow
        };
        _db.LoginAttempts.Add(attempt);
        await _db.SaveChangesAsync();
        await _broadcast.AttemptAsync(new { attempt.IP, type = type.ToString(), attempt.Username, attempt.Success, attempt.Country, at = attempt.Timestamp });

        // Whitelisted IPs are never blocked.
        if (await _db.IpAccessRules.AnyAsync(r => r.Kind == IpRuleKind.WhitelistIp && r.Value == ip)) return;

        if (!success)
        {
            var settings = await _db.SecuritySettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1);
            var max = settings?.BruteForceMaxAttempts ?? 5;
            var recentFails = await GetFailedAttemptsAsync(ip, 1);
            if (recentFails >= max && !await IsBlockedAsync(ip))
            {
                await BlockIpAsync(ip, TimeSpan.FromMinutes(settings?.BruteForceBlockMinutes ?? 30),
                    $"Exceeded {max} failed {type} attempts", manual: false);
            }
        }
    }

    public Task<bool> IsBlockedAsync(string ip) =>
        _db.BlockedIPs.AnyAsync(b => b.IP == ip && b.UnblockedAt == null && (b.ExpiresAt == null || b.ExpiresAt > DateTime.UtcNow));

    public async Task BlockIpAsync(string ip, TimeSpan? duration, string reason, bool manual)
    {
        var block = new BlockedIP
        {
            IP = ip.Trim(), Reason = reason, IsManual = manual, BlockedAt = DateTime.UtcNow,
            ExpiresAt = duration.HasValue ? DateTime.UtcNow.Add(duration.Value) : null, Country = GeoLookup(ip)
        };
        _db.BlockedIPs.Add(block);
        await _db.SaveChangesAsync();
        await _runner.RunAsync($"ufw insert 1 deny from {ip}", ServiceName);
    }

    public async Task UnblockIpAsync(int blockedId)
    {
        var b = await _db.BlockedIPs.FindAsync(blockedId);
        if (b == null || b.UnblockedAt != null) return;
        b.UnblockedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _runner.RunAsync($"ufw delete deny from {b.IP}", ServiceName);
    }

    public Task<List<BlockedIP>> GetBlockedIpsAsync() =>
        _db.BlockedIPs.Where(b => b.UnblockedAt == null && (b.ExpiresAt == null || b.ExpiresAt > DateTime.UtcNow))
            .OrderByDescending(b => b.BlockedAt).ToListAsync();

    public Task<int> GetFailedAttemptsAsync(string ip, int hours)
    {
        var since = DateTime.UtcNow.AddHours(-hours);
        return _db.LoginAttempts.CountAsync(a => a.IP == ip && !a.Success && a.Timestamp >= since);
    }

    public async Task<AttackReport> GetAttackReportAsync(DateTime from, DateTime to)
    {
        var attempts = await _db.LoginAttempts.Where(a => a.Timestamp >= from && a.Timestamp <= to).ToListAsync();
        var topIps = attempts.Where(a => !a.Success).GroupBy(a => a.IP)
            .Select(g => new TopIp(g.Key, g.Count(), g.First().Country))
            .OrderByDescending(t => t.Count).Take(10).ToList();
        var topCountries = attempts.Where(a => !a.Success && a.Country != null).GroupBy(a => a.Country!)
            .Select(g => new TopCountry(g.Key, g.Count()))
            .OrderByDescending(t => t.Count).Take(10).ToList();
        var blocked = await _db.BlockedIPs.CountAsync(b => b.BlockedAt >= from && b.BlockedAt <= to);
        return new AttackReport(attempts.Count, attempts.Count(a => !a.Success), blocked, topIps, topCountries);
    }

    public Task<List<LoginAttempt>> GetRecentAttemptsAsync(int take = 50) =>
        _db.LoginAttempts.OrderByDescending(a => a.Timestamp).Take(take).ToListAsync();

    // Lightweight deterministic pseudo-GeoIP (real deployments use a MaxMind DB).
    private static readonly string[] Countries = { "US", "RU", "CN", "DE", "BR", "IN", "NL", "FR", "GB", "TR" };
    private static string GeoLookup(string ip)
    {
        if (string.IsNullOrEmpty(ip) || ip is "127.0.0.1" or "::1" || ip.StartsWith("192.168.") || ip.StartsWith("10."))
            return "Local";
        return Countries[Math.Abs(ip.GetHashCode()) % Countries.Length];
    }
}
