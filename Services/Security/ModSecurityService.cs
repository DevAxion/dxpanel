using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Security;

public record WafStatus(bool Enabled, WafMode Mode, int RuleCount, string CrsVersion);

public interface IModSecurityService
{
    Task<WafStatus> GetStatusAsync(int domainId);
    Task EnableAsync(int domainId);
    Task DisableAsync(int domainId);
    Task SetModeAsync(int domainId, WafMode mode);
    Task<List<ModSecurityAlert>> GetAlertsAsync(int domainId, DateTime from, DateTime to);
    Task<WafCustomRule> AddCustomRuleAsync(int domainId, string ruleText, string? description);
    Task RemoveCustomRuleAsync(int domainId, int ruleId);
    Task<List<WafCustomRule>> GetCustomRulesAsync(int domainId);
    Task AddIpRuleAsync(int? domainId, string ip, WafIpAction action);
    Task RemoveIpRuleAsync(int ruleId);
    Task<List<WafIpRule>> GetIpRulesAsync(int domainId);
    Task<string> UpdateCrsAsync();
}

/// <summary>
/// Manages ModSecurity (WAF) config for Nginx per domain. Simulation-safe: config
/// writes and reloads go through <see cref="ICommandRunner"/>, which no-ops on dev.
/// </summary>
public class ModSecurityService : IModSecurityService
{
    private const string ServiceName = "modsecurity";
    private readonly ApplicationDbContext _db;
    private readonly ICommandRunner _runner;

    public ModSecurityService(ApplicationDbContext db, ICommandRunner runner)
    {
        _db = db;
        _runner = runner;
    }

    private async Task<WafConfig> GetOrCreateAsync(int domainId)
    {
        var cfg = await _db.WafConfigs.FirstOrDefaultAsync(c => c.DomainId == domainId);
        if (cfg == null)
        {
            cfg = new WafConfig { DomainId = domainId, Enabled = false, Mode = WafMode.Detection };
            _db.WafConfigs.Add(cfg);
            await _db.SaveChangesAsync();
        }
        return cfg;
    }

    private async Task<string> CrsVersionAsync() =>
        (await _db.SecuritySettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1))?.CrsVersion ?? "4.3.0";

    public async Task<WafStatus> GetStatusAsync(int domainId)
    {
        var cfg = await GetOrCreateAsync(domainId);
        var ruleCount = 900 + await _db.WafCustomRules.CountAsync(r => r.DomainId == domainId); // OWASP CRS ~900 rules + custom
        return new WafStatus(cfg.Enabled, cfg.Mode, ruleCount, await CrsVersionAsync());
    }

    public async Task EnableAsync(int domainId)
    {
        var cfg = await GetOrCreateAsync(domainId);
        cfg.Enabled = true;
        cfg.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await WriteConfigAsync(domainId, cfg);
    }

    public async Task DisableAsync(int domainId)
    {
        var cfg = await GetOrCreateAsync(domainId);
        cfg.Enabled = false;
        cfg.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await WriteConfigAsync(domainId, cfg);
    }

    public async Task SetModeAsync(int domainId, WafMode mode)
    {
        var cfg = await GetOrCreateAsync(domainId);
        cfg.Mode = mode;
        cfg.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await WriteConfigAsync(domainId, cfg);
    }

    private async Task WriteConfigAsync(int domainId, WafConfig cfg)
    {
        var domain = await _db.Domains.FindAsync(domainId);
        var name = domain?.DomainName ?? $"domain-{domainId}";
        var rule = cfg.Mode == WafMode.Prevention ? "On" : "DetectionOnly";
        var conf = $"# ModSecurity for {name}\nSecRuleEngine {(cfg.Enabled ? rule : "Off")}\nInclude /etc/modsecurity/crs/crs-setup.conf\n";
        await _runner.WriteFileAsync($"/etc/nginx/modsec/{name}.conf", conf, ServiceName);
        await _runner.RunAsync("systemctl reload nginx", ServiceName);
    }

    public Task<List<ModSecurityAlert>> GetAlertsAsync(int domainId, DateTime from, DateTime to) =>
        _db.ModSecurityAlerts.Where(a => a.DomainId == domainId && a.Timestamp >= from && a.Timestamp <= to)
            .OrderByDescending(a => a.Timestamp).Take(500).ToListAsync();

    public async Task<WafCustomRule> AddCustomRuleAsync(int domainId, string ruleText, string? description)
    {
        var next = 900000 + await _db.WafCustomRules.CountAsync() + 1;
        var rule = new WafCustomRule { DomainId = domainId, RuleNumber = next, RuleText = ruleText, Description = description, CreatedAt = DateTime.UtcNow };
        _db.WafCustomRules.Add(rule);
        await _db.SaveChangesAsync();
        await _runner.LogExternalAsync($"modsec.addRule(domain={domainId}, id={next})", "custom rule added", true, ServiceName);
        return rule;
    }

    public async Task RemoveCustomRuleAsync(int domainId, int ruleId)
    {
        var rule = await _db.WafCustomRules.FirstOrDefaultAsync(r => r.Id == ruleId && r.DomainId == domainId);
        if (rule != null) { _db.WafCustomRules.Remove(rule); await _db.SaveChangesAsync(); }
    }

    public Task<List<WafCustomRule>> GetCustomRulesAsync(int domainId) =>
        _db.WafCustomRules.Where(r => r.DomainId == domainId).OrderBy(r => r.RuleNumber).ToListAsync();

    public async Task AddIpRuleAsync(int? domainId, string ip, WafIpAction action)
    {
        _db.WafIpRules.Add(new WafIpRule { DomainId = domainId, IP = ip.Trim(), Action = action, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        await _runner.LogExternalAsync($"modsec.ipRule({action}, {ip})", "ip rule added", true, ServiceName);
    }

    public async Task RemoveIpRuleAsync(int ruleId)
    {
        var rule = await _db.WafIpRules.FindAsync(ruleId);
        if (rule != null) { _db.WafIpRules.Remove(rule); await _db.SaveChangesAsync(); }
    }

    public Task<List<WafIpRule>> GetIpRulesAsync(int domainId) =>
        _db.WafIpRules.Where(r => r.DomainId == domainId || r.DomainId == null).OrderByDescending(r => r.CreatedAt).ToListAsync();

    public async Task<string> UpdateCrsAsync()
    {
        var settings = await _db.SecuritySettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (settings != null)
        {
            settings.CrsVersion = "4.3.0";
            settings.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        await _runner.RunAsync("cd /etc/modsecurity/crs && git pull && systemctl reload nginx", ServiceName);
        return settings?.CrsVersion ?? "4.3.0";
    }
}
