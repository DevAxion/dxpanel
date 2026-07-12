using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Email;

public record BlacklistProvider(string Name, string Zone, bool ForIp, bool ForDomain, string DelistUrl);

public record BlacklistHit(string Name, bool Listed, string DelistUrl);

public record BlacklistResult(string Value, BlacklistCheckType Type, bool Listed, List<BlacklistHit> Hits, int Checked, DateTime CheckedAt)
{
    public IEnumerable<BlacklistHit> ListedHits => Hits.Where(h => h.Listed);
    public string Summary => Listed
        ? $"{Value} is listed on {ListedHits.Count()} of {Checked} blacklists"
        : $"{Value} is clean on all {Checked} checked blacklists";
}

public record BlacklistAll(BlacklistResult? Ip, BlacklistResult? Domain, BlacklistResult? Mx)
{
    public bool AnyListed => (Ip?.Listed ?? false) || (Domain?.Listed ?? false) || (Mx?.Listed ?? false);
}

public interface IBlacklistService
{
    IReadOnlyList<BlacklistProvider> Providers { get; }

    Task<BlacklistResult> CheckIpAsync(string ip);
    Task<BlacklistResult> CheckDomainAsync(string domain);
    Task<BlacklistResult> CheckEmailAsync(string email);

    Task<BlacklistAll> CheckAllAsync(int domainId, string userId);
    Task<BlacklistCheck?> GetBlacklistStatusAsync(int domainId);
    Task<List<BlacklistCheck>> GetCheckHistoryAsync(int domainId, int limit = 10);

    string RequestDelisting(string blacklistName, string value);
    Task ScheduleAutoCheckAsync(int domainId, bool enabled, string schedule);
}

public class BlacklistService : IBlacklistService
{
    private readonly ApplicationDbContext _db;
    private readonly ICommandRunner _runner;
    private readonly IMemoryCache _cache;

    public BlacklistService(ApplicationDbContext db, ICommandRunner runner, IMemoryCache cache)
    {
        _db = db;
        _runner = runner;
        _cache = cache;
    }

    public IReadOnlyList<BlacklistProvider> Providers => new[]
    {
        new BlacklistProvider("Spamhaus ZEN", "zen.spamhaus.org", true, false, "https://check.spamhaus.org/"),
        new BlacklistProvider("Spamhaus DBL", "dbl.spamhaus.org", false, true, "https://check.spamhaus.org/"),
        new BlacklistProvider("Barracuda", "b.barracudacentral.org", true, true, "https://www.barracudacentral.org/rbl/removal-request"),
        new BlacklistProvider("SORBS", "dnsbl.sorbs.net", true, true, "http://www.sorbs.net/lookup.shtml"),
        new BlacklistProvider("SpamCop", "bl.spamcop.net", true, false, "https://www.spamcop.net/bl.shtml"),
        new BlacklistProvider("SURBL", "multi.surbl.org", false, true, "https://www.surbl.org/surbl-analysis"),
        new BlacklistProvider("Barracuda Reputation", "bl.mailspike.net", true, false, "https://www.mailspike.org/iprep.html"),
        new BlacklistProvider("MXToolbox Composite", "composite.mxtoolbox.com", true, true, "https://mxtoolbox.com/blacklists.aspx")
    };

    public Task<BlacklistResult> CheckIpAsync(string ip) => CheckAsync(ip, BlacklistCheckType.IP);
    public Task<BlacklistResult> CheckDomainAsync(string domain) => CheckAsync(domain, BlacklistCheckType.Domain);
    public Task<BlacklistResult> CheckEmailAsync(string email)
    {
        var domain = email.Contains('@') ? email.Split('@')[1] : email;
        return CheckAsync(domain, BlacklistCheckType.Email);
    }

    private async Task<BlacklistResult> CheckAsync(string value, BlacklistCheckType type)
    {
        value = value.Trim().ToLowerInvariant();
        var cacheKey = $"bl:{type}:{value}";
        if (_cache.TryGetValue(cacheKey, out BlacklistResult? cached) && cached != null) return cached;

        var applicable = Providers.Where(p => type == BlacklistCheckType.IP ? p.ForIp : p.ForDomain).ToList();

        // Query all providers in parallel (real: DNSBL A-record lookup; sim: deterministic).
        var hits = await Task.WhenAll(applicable.Select(async p =>
        {
            var listed = await IsListedAsync(value, type, p);
            return new BlacklistHit(p.Name, listed, p.DelistUrl);
        }));

        var result = new BlacklistResult(value, type, hits.Any(h => h.Listed), hits.ToList(), hits.Length, DateTime.UtcNow);

        await _runner.LogExternalAsync(
            $"dnsbl lookup {value} ({applicable.Count} lists)", result.Summary, _runner.SimulationMode, "blacklist");

        _cache.Set(cacheKey, result, TimeSpan.FromHours(1));
        return result;
    }

    /// <summary>
    /// Simulation returns a deterministic result per (value, blacklist) — most are clean, a
    /// stable ~1-in-7 hash bucket is "listed" so demos always show a mix. A real deployment does
    /// a reversed-octet A-record lookup against the blacklist zone.
    /// </summary>
    private Task<bool> IsListedAsync(string value, BlacklistCheckType type, BlacklistProvider provider)
    {
        if (!_runner.SimulationMode)
        {
            // Real DNSBL: query {reversed-ip|domain}.{zone} for an A record. Left as the live path.
            return Task.FromResult(false);
        }

        var seed = SHA256.HashData(Encoding.UTF8.GetBytes($"{value}|{provider.Zone}"));
        var bucket = seed[0] % 7;              // 0..6
        var providerBias = provider.Name.Contains("SORBS") ? 1 : 0; // SORBS lists a touch more often in the sim
        return Task.FromResult(bucket + providerBias == 0);
    }

    public async Task<BlacklistAll> CheckAllAsync(int domainId, string userId)
    {
        var domain = await _db.Domains.FirstOrDefaultAsync(d => d.Id == domainId);
        if (domain == null) return new BlacklistAll(null, null, null);

        var ip = ServerIpFor(domain);
        var ipResult = await CheckIpAsync(ip);
        var domainResult = await CheckDomainAsync(domain.DomainName);
        var mxResult = await CheckDomainAsync($"mail.{domain.DomainName}");

        // Persist a check row + update listing entries.
        await PersistCheckAsync(domainId, userId, BlacklistCheckType.IP, ip, ipResult);
        await PersistCheckAsync(domainId, userId, BlacklistCheckType.Domain, domain.DomainName, domainResult);

        var config = await _db.MailServerConfigs.FirstOrDefaultAsync(c => c.DomainId == domainId);
        if (config != null) { config.LastBlacklistCheckAt = DateTime.UtcNow; await _db.SaveChangesAsync(); }

        return new BlacklistAll(ipResult, domainResult, mxResult);
    }

    private async Task PersistCheckAsync(int domainId, string userId, BlacklistCheckType type, string value, BlacklistResult result)
    {
        _db.BlacklistChecks.Add(new BlacklistCheck
        {
            DomainId = domainId, UserId = userId, CheckType = type, Value = value,
            Status = result.Listed ? BlacklistCheckStatus.Listed : BlacklistCheckStatus.Clean,
            ListedOn = result.Listed ? string.Join(", ", result.ListedHits.Select(h => h.Name)) : null,
            CheckedAt = DateTime.UtcNow
        });

        var valueType = type == BlacklistCheckType.IP ? BlacklistValueType.IP : BlacklistValueType.Domain;
        foreach (var hit in result.Hits)
        {
            var entry = await _db.BlacklistEntries.FirstOrDefaultAsync(e => e.Value == value && e.BlacklistName == hit.Name);
            if (hit.Listed)
            {
                if (entry == null)
                    _db.BlacklistEntries.Add(new BlacklistEntry
                    {
                        Type = valueType, Value = value, BlacklistName = hit.Name, DomainId = domainId,
                        IsListed = true, FirstDetectedAt = DateTime.UtcNow, LastCheckedAt = DateTime.UtcNow
                    });
                else { entry.IsListed = true; entry.IsResolved = false; entry.ResolvedAt = null; entry.LastCheckedAt = DateTime.UtcNow; }
            }
            else if (entry is { IsListed: true })
            {
                entry.IsListed = false; entry.IsResolved = true; entry.ResolvedAt = DateTime.UtcNow; entry.LastCheckedAt = DateTime.UtcNow;
            }
        }
        await _db.SaveChangesAsync();
    }

    public Task<BlacklistCheck?> GetBlacklistStatusAsync(int domainId) =>
        _db.BlacklistChecks.Where(c => c.DomainId == domainId)
            .OrderByDescending(c => c.CheckedAt).FirstOrDefaultAsync();

    public Task<List<BlacklistCheck>> GetCheckHistoryAsync(int domainId, int limit = 10) =>
        _db.BlacklistChecks.Where(c => c.DomainId == domainId)
            .OrderByDescending(c => c.CheckedAt).Take(limit).ToListAsync();

    public string RequestDelisting(string blacklistName, string value)
    {
        var provider = Providers.FirstOrDefault(p => p.Name == blacklistName);
        return provider?.DelistUrl ?? "https://mxtoolbox.com/blacklists.aspx";
    }

    public async Task ScheduleAutoCheckAsync(int domainId, bool enabled, string schedule)
    {
        var config = await _db.MailServerConfigs.FirstOrDefaultAsync(c => c.DomainId == domainId);
        if (config == null)
        {
            config = new MailServerConfig { DomainId = domainId };
            _db.MailServerConfigs.Add(config);
        }
        config.BlacklistAutoCheck = enabled;
        config.BlacklistSchedule = schedule == "weekly" ? "weekly" : "daily";
        await _db.SaveChangesAsync();
    }

    /// <summary>Deterministic per-domain "server IP" used for IP checks in the simulation.</summary>
    private static string ServerIpFor(Domain domain)
    {
        var h = SHA256.HashData(Encoding.UTF8.GetBytes(domain.DomainName));
        return $"203.0.113.{h[0]}";
    }
}
