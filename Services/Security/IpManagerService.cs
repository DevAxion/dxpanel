using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Security;

public interface IIpManagerService
{
    Task<List<IpAccessRule>> GetRulesAsync(IpRuleKind kind);
    Task AddRuleAsync(IpRuleKind kind, string value, string? reason);
    Task RemoveRuleAsync(int ruleId);
    Task<string> GetFirewallStatusAsync();
    Task<int> GetRateLimitAsync();
    Task SetRateLimitAsync(int perMinute);
}

/// <summary>
/// Global IP whitelist/blacklist, country blocking, rate-limit config and a UFW
/// firewall viewer. Simulation-safe: UFW commands run through ICommandRunner.
/// </summary>
public class IpManagerService : IIpManagerService
{
    private const string ServiceName = "ufw";
    private readonly ApplicationDbContext _db;
    private readonly ICommandRunner _runner;

    public IpManagerService(ApplicationDbContext db, ICommandRunner runner)
    {
        _db = db;
        _runner = runner;
    }

    public Task<List<IpAccessRule>> GetRulesAsync(IpRuleKind kind) =>
        _db.IpAccessRules.Where(r => r.Kind == kind).OrderByDescending(r => r.CreatedAt).ToListAsync();

    public async Task AddRuleAsync(IpRuleKind kind, string value, string? reason)
    {
        value = value.Trim();
        if (string.IsNullOrEmpty(value)) return;
        if (await _db.IpAccessRules.AnyAsync(r => r.Kind == kind && r.Value == value)) return;

        _db.IpAccessRules.Add(new IpAccessRule { Kind = kind, Value = value, Reason = reason, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var cmd = kind switch
        {
            IpRuleKind.WhitelistIp => $"ufw insert 1 allow from {value}",
            IpRuleKind.BlacklistIp => $"ufw insert 1 deny from {value}",
            IpRuleKind.BlockCountry => $"srx-geoblock add {value}",
            _ => ""
        };
        if (!string.IsNullOrEmpty(cmd)) await _runner.RunAsync(cmd, ServiceName);
    }

    public async Task RemoveRuleAsync(int ruleId)
    {
        var rule = await _db.IpAccessRules.FindAsync(ruleId);
        if (rule == null) return;
        _db.IpAccessRules.Remove(rule);
        await _db.SaveChangesAsync();
        var cmd = rule.Kind switch
        {
            IpRuleKind.WhitelistIp => $"ufw delete allow from {rule.Value}",
            IpRuleKind.BlacklistIp => $"ufw delete deny from {rule.Value}",
            IpRuleKind.BlockCountry => $"srx-geoblock remove {rule.Value}",
            _ => ""
        };
        if (!string.IsNullOrEmpty(cmd)) await _runner.RunAsync(cmd, ServiceName);
    }

    public async Task<string> GetFirewallStatusAsync()
    {
        var cmd = await _runner.RunAsync("ufw status verbose", ServiceName);
        return cmd.Simulated
            ? "Status: active (simulated)\nDefault: deny (incoming), allow (outgoing)\n22/tcp ALLOW · 80/tcp ALLOW · 443/tcp ALLOW"
            : cmd.Output;
    }

    public async Task<int> GetRateLimitAsync() =>
        (await _db.SecuritySettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1))?.RateLimitPerMinute ?? 120;

    public async Task SetRateLimitAsync(int perMinute)
    {
        var settings = await _db.SecuritySettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (settings != null)
        {
            settings.RateLimitPerMinute = Math.Clamp(perMinute, 10, 100000);
            settings.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }
}
