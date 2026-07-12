using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Cloudflare;

/// <summary>One side of a DNS comparison between the panel zone and the Cloudflare zone.</summary>
public record DnsDiffRow(string Type, string Name, string? PanelValue, string? CloudflareValue, string State);

public enum DnsSyncDirection
{
    PanelToCloudflare,
    CloudflareToPanel,
    Merge
}

public interface ICloudflareManager
{
    Task<CloudflareAccount?> GetAccountAsync(string userId);
    Task<CfTokenResult> ConnectAsync(string userId, string apiToken, string? email);
    Task DisconnectAsync(string userId);

    Task<List<CfZone>> ListRemoteZonesAsync(string userId);
    Task<List<Domain>> GetLinkableDomainsAsync(string userId);
    Task<List<CloudflareDomain>> GetLinkedDomainsAsync(string userId);
    Task<CloudflareDomain?> GetLinkedDomainAsync(string userId, int domainId);
    Task<CloudflareDomain?> GetByIdAsync(string userId, int cloudflareDomainId);

    /// <summary>Links a panel domain to a Cloudflare zone (existing zone id, or empty to create one).</summary>
    Task<CloudflareDomain> LinkDomainAsync(string userId, int domainId, string? existingZoneId);
    Task UnlinkDomainAsync(string userId, int cloudflareDomainId);

    Task RefreshStatusAsync(CloudflareDomain cf);

    // DNS
    Task<List<DnsDiffRow>> CompareDnsAsync(string userId, CloudflareDomain cf);
    Task<int> SyncDnsAsync(string userId, CloudflareDomain cf, DnsSyncDirection direction);
    Task<List<CfDnsRecord>> GetRecordsAsync(CloudflareDomain cf);
    Task<CfResult> ToggleProxyAsync(CloudflareDomain cf, string recordId, string type, string name, string content, int ttl, bool proxied);

    // Cache
    Task<CfResult> PurgeAllAsync(string userId, CloudflareDomain cf);
    Task<CfResult> PurgeUrlsAsync(string userId, CloudflareDomain cf, IEnumerable<string> urls);

    // Settings snapshot writers (push to CF + persist the snapshot column)
    Task<CfResult> ApplyAsync(CloudflareDomain cf, Func<ICloudflareService, CloudflareAccount, string, Task<CfResult>> push, Action<CloudflareDomain> mutate);

    ICloudflareService Gateway { get; }

    // WordPress integration
    Task<bool> IsWordPressAsync(int domainId);
    Task<CfResult> PurgeOnPublishAsync(int domainId);
    Task ApplyWordPressPresetAsync(string userId, CloudflareDomain cf);
}

public class CloudflareManager : ICloudflareManager
{
    private readonly ApplicationDbContext _db;
    private readonly ICloudflareService _cf;
    private readonly INotificationService _notifications;

    public CloudflareManager(ApplicationDbContext db, ICloudflareService cf, INotificationService notifications)
    {
        _db = db;
        _cf = cf;
        _notifications = notifications;
    }

    public ICloudflareService Gateway => _cf;

    public Task<CloudflareAccount?> GetAccountAsync(string userId) =>
        _db.CloudflareAccounts.FirstOrDefaultAsync(a => a.UserId == userId && a.IsActive);

    public async Task<CfTokenResult> ConnectAsync(string userId, string apiToken, string? email)
    {
        var validation = await _cf.ValidateTokenAsync(apiToken);
        if (!validation.Valid) return validation;

        var account = await _db.CloudflareAccounts.FirstOrDefaultAsync(a => a.UserId == userId);
        if (account == null)
        {
            account = new CloudflareAccount { UserId = userId };
            _db.CloudflareAccounts.Add(account);
        }

        account.ApiToken = apiToken.Trim();
        account.Email = email ?? validation.Email;
        account.AccountId = validation.AccountId ?? "";
        account.AccountName = validation.AccountName;
        account.TokenScopes = string.Join(", ", validation.Scopes);
        account.IsActive = true;
        account.LastValidatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return validation;
    }

    public async Task DisconnectAsync(string userId)
    {
        var account = await _db.CloudflareAccounts.FirstOrDefaultAsync(a => a.UserId == userId);
        if (account == null) return;

        // Linked domains cascade; the Cloudflare zones themselves are left intact.
        _db.CloudflareAccounts.Remove(account);
        await _db.SaveChangesAsync();
    }

    public async Task<List<CfZone>> ListRemoteZonesAsync(string userId)
    {
        var account = await GetAccountAsync(userId);
        return account == null ? new List<CfZone>() : await _cf.GetZonesAsync(account);
    }

    public async Task<List<Domain>> GetLinkableDomainsAsync(string userId)
    {
        var linked = await _db.CloudflareDomains
            .Where(c => c.Account!.UserId == userId)
            .Select(c => c.DomainId).ToListAsync();

        return await _db.Domains
            .Where(d => d.UserId == userId && !linked.Contains(d.Id))
            .OrderBy(d => d.DomainName).ToListAsync();
    }

    public Task<List<CloudflareDomain>> GetLinkedDomainsAsync(string userId) =>
        _db.CloudflareDomains.Include(c => c.Domain).Include(c => c.Account)
            .Where(c => c.Account!.UserId == userId)
            .OrderBy(c => c.Domain!.DomainName).ToListAsync();

    public Task<CloudflareDomain?> GetLinkedDomainAsync(string userId, int domainId) =>
        _db.CloudflareDomains.Include(c => c.Domain).Include(c => c.Account)
            .FirstOrDefaultAsync(c => c.DomainId == domainId && c.Account!.UserId == userId);

    public Task<CloudflareDomain?> GetByIdAsync(string userId, int cloudflareDomainId) =>
        _db.CloudflareDomains.Include(c => c.Domain).Include(c => c.Account)
            .FirstOrDefaultAsync(c => c.Id == cloudflareDomainId && c.Account!.UserId == userId);

    public async Task<CloudflareDomain> LinkDomainAsync(string userId, int domainId, string? existingZoneId)
    {
        var account = await GetAccountAsync(userId)
            ?? throw new InvalidOperationException("Connect a Cloudflare account first.");

        var domain = await _db.Domains.FirstOrDefaultAsync(d => d.Id == domainId && d.UserId == userId)
            ?? throw new InvalidOperationException("Domain not found.");

        if (await _db.CloudflareDomains.AnyAsync(c => c.DomainId == domainId))
            throw new InvalidOperationException($"{domain.DomainName} is already linked to Cloudflare.");

        string zoneId;
        string? ns1, ns2;
        CloudflareZoneStatus status;

        if (!string.IsNullOrWhiteSpace(existingZoneId))
        {
            var zones = await _cf.GetZonesAsync(account);
            var zone = zones.FirstOrDefault(z => z.ZoneId == existingZoneId)
                ?? throw new InvalidOperationException("That zone is not in your Cloudflare account.");
            zoneId = zone.ZoneId;
            ns1 = zone.NameServer1;
            ns2 = zone.NameServer2;
            status = ParseStatus(zone.Status);
        }
        else
        {
            var zone = await _cf.AddZoneAsync(account, domain.DomainName)
                ?? throw new InvalidOperationException("Cloudflare could not create the zone.");
            zoneId = zone.ZoneId;
            ns1 = zone.NameServer1;
            ns2 = zone.NameServer2;
            status = CloudflareZoneStatus.Pending;
        }

        var cf = new CloudflareDomain
        {
            DomainId = domain.Id,
            CloudflareAccountId = account.Id,
            ZoneId = zoneId,
            Status = status,
            NameServer1 = ns1,
            NameServer2 = ns2,
            SyncedAt = DateTime.UtcNow
        };

        _db.CloudflareDomains.Add(cf);
        await _db.SaveChangesAsync();

        await _notifications.NotifyAsync(userId, "Cloudflare connected",
            $"{domain.DomainName} is now managed through Cloudflare (zone {status}).", NotificationType.Success);

        return cf;
    }

    public async Task UnlinkDomainAsync(string userId, int cloudflareDomainId)
    {
        var cf = await GetByIdAsync(userId, cloudflareDomainId);
        if (cf == null) return;
        _db.CloudflareDomains.Remove(cf);
        await _db.SaveChangesAsync();
    }

    public async Task RefreshStatusAsync(CloudflareDomain cf)
    {
        var previous = cf.Status;
        cf.Status = await _cf.GetZoneStatusAsync(cf.Account!, cf.ZoneId);
        cf.SyncedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        if (previous != cf.Status)
            await _notifications.NotifyAsync(cf.Account!.UserId, "Cloudflare zone status changed",
                $"{cf.Domain?.DomainName}: {previous} → {cf.Status}.", NotificationType.Info,
                $"cf-status-{cf.Id}-{cf.Status}");
    }

    // ---------------- DNS ----------------

    public Task<List<CfDnsRecord>> GetRecordsAsync(CloudflareDomain cf) =>
        _cf.GetRecordsAsync(cf.Account!, cf.ZoneId);

    public async Task<List<DnsDiffRow>> CompareDnsAsync(string userId, CloudflareDomain cf)
    {
        var domainName = cf.Domain!.DomainName;

        var panel = await _db.DnsRecords
            .Where(r => r.Zone!.DomainId == cf.DomainId)
            .ToListAsync();

        var remote = await _cf.GetRecordsAsync(cf.Account!, cf.ZoneId);

        // Key on type + normalized host so "@" and the apex FQDN line up.
        string Key(string type, string name)
        {
            var host = name.Replace($".{domainName}", "").Trim();
            if (host == domainName || host == "") host = "@";
            return $"{type}|{host}";
        }

        var rows = new Dictionary<string, DnsDiffRow>();

        foreach (var r in panel)
        {
            var key = Key(r.Type.ToString(), r.Name);
            rows[key] = new DnsDiffRow(r.Type.ToString(), r.Name == "" ? "@" : r.Name, r.Value, null, "panel-only");
        }

        foreach (var r in remote)
        {
            var host = r.Name.Replace($".{domainName}", "").Trim();
            if (host == domainName) host = "@";
            var key = Key(r.Type, host);

            if (rows.TryGetValue(key, out var existing))
            {
                var state = existing.PanelValue == r.Content ? "match" : "differs";
                rows[key] = existing with { CloudflareValue = r.Content, State = state };
            }
            else
            {
                rows[key] = new DnsDiffRow(r.Type, host, null, r.Content, "cloudflare-only");
            }
        }

        return rows.Values.OrderBy(r => r.Type).ThenBy(r => r.Name).ToList();
    }

    public async Task<int> SyncDnsAsync(string userId, CloudflareDomain cf, DnsSyncDirection direction)
    {
        var domainName = cf.Domain!.DomainName;
        var changes = 0;

        var panel = await _db.DnsRecords.Include(r => r.Zone)
            .Where(r => r.Zone!.DomainId == cf.DomainId).ToListAsync();
        var remote = await _cf.GetRecordsAsync(cf.Account!, cf.ZoneId);

        bool RemoteHas(DnsRecord r) =>
            remote.Any(x => x.Type == r.Type.ToString() &&
                            NormalizeHost(x.Name, domainName) == NormalizeHost(r.Name, domainName) &&
                            x.Content == r.Value);

        if (direction is DnsSyncDirection.PanelToCloudflare or DnsSyncDirection.Merge)
        {
            foreach (var r in panel.Where(r => !RemoteHas(r)))
            {
                var proxied = r.Type is DnsRecordType.A or DnsRecordType.AAAA or DnsRecordType.CNAME;
                var created = await _cf.CreateRecordAsync(cf.Account!, cf.ZoneId, r.Type.ToString(),
                    r.Name == "" ? domainName : r.Name, r.Value, r.TTL == 3600 ? 1 : r.TTL, proxied, r.Priority);
                if (created != null) changes++;
            }
        }

        if (direction is DnsSyncDirection.CloudflareToPanel or DnsSyncDirection.Merge)
        {
            var zone = await _db.DnsZones.FirstOrDefaultAsync(z => z.DomainId == cf.DomainId);
            if (zone == null)
            {
                zone = new DnsZone { DomainId = cf.DomainId, UserId = userId, CreatedAt = DateTime.UtcNow };
                _db.DnsZones.Add(zone);
                await _db.SaveChangesAsync();
            }

            foreach (var r in remote)
            {
                if (!Enum.TryParse<DnsRecordType>(r.Type, out var type)) continue;
                var host = NormalizeHost(r.Name, domainName);

                var exists = panel.Any(p => p.Type == type &&
                                            NormalizeHost(p.Name, domainName) == host &&
                                            p.Value == r.Content);
                if (exists) continue;

                _db.DnsRecords.Add(new DnsRecord
                {
                    ZoneId = zone.Id,
                    Type = type,
                    Name = host == "@" ? "" : host,
                    Value = r.Content,
                    TTL = r.Ttl <= 1 ? 3600 : r.Ttl,
                    Priority = r.Priority,
                    CreatedAt = DateTime.UtcNow
                });
                changes++;
            }
        }

        cf.SyncedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return changes;
    }

    public Task<CfResult> ToggleProxyAsync(CloudflareDomain cf, string recordId, string type, string name, string content, int ttl, bool proxied) =>
        _cf.UpdateRecordAsync(cf.Account!, cf.ZoneId, recordId, type, name, content, ttl, proxied);

    // ---------------- Cache ----------------

    public async Task<CfResult> PurgeAllAsync(string userId, CloudflareDomain cf)
    {
        var result = await _cf.PurgeAllAsync(cf.Account!, cf.ZoneId);
        if (result.Success) await RecordPurgeAsync(cf, CfPurgeType.Everything, "all", userId);
        return result;
    }

    public async Task<CfResult> PurgeUrlsAsync(string userId, CloudflareDomain cf, IEnumerable<string> urls)
    {
        var list = urls.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()).ToList();
        if (list.Count == 0) return new CfResult(false, "Enter at least one URL.", false);

        var result = await _cf.PurgeUrlsAsync(cf.Account!, cf.ZoneId, list);
        if (result.Success) await RecordPurgeAsync(cf, CfPurgeType.Urls, string.Join("\n", list), userId);
        return result;
    }

    private async Task RecordPurgeAsync(CloudflareDomain cf, CfPurgeType type, string detail, string userId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        _db.CloudflareCaches.Add(new CloudflareCache
        {
            CloudflareDomainId = cf.Id,
            PurgeType = type,
            Detail = detail.Length > 1000 ? detail[..1000] : detail,
            PurgedBy = user?.UserName,
            LastPurgedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    // ---------------- Settings ----------------

    public async Task<CfResult> ApplyAsync(CloudflareDomain cf,
        Func<ICloudflareService, CloudflareAccount, string, Task<CfResult>> push, Action<CloudflareDomain> mutate)
    {
        var result = await push(_cf, cf.Account!, cf.ZoneId);
        if (result.Success)
        {
            mutate(cf);
            cf.SyncedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        return result;
    }

    // ---------------- WordPress ----------------

    public Task<bool> IsWordPressAsync(int domainId) =>
        _db.AppInstallations.Include(i => i.AppDefinition)
            .AnyAsync(i => i.DomainId == domainId &&
                           (i.AppDefinition!.Slug == "wordpress" || i.AppDefinition.Slug == "woocommerce"));

    public async Task<CfResult> PurgeOnPublishAsync(int domainId)
    {
        var cf = await _db.CloudflareDomains.Include(c => c.Account).Include(c => c.Domain)
            .FirstOrDefaultAsync(c => c.DomainId == domainId);
        if (cf == null) return new CfResult(false, "Domain is not linked to Cloudflare.", false);

        var result = await _cf.PurgeAllAsync(cf.Account!, cf.ZoneId);
        if (result.Success) await RecordPurgeAsync(cf, CfPurgeType.Everything, "auto: WordPress post published", cf.Account!.UserId);
        return result;
    }

    /// <summary>Applies the WordPress-recommended Cloudflare configuration in one shot.</summary>
    public async Task ApplyWordPressPresetAsync(string userId, CloudflareDomain cf)
    {
        var isWoo = await _db.AppInstallations.Include(i => i.AppDefinition)
            .AnyAsync(i => i.DomainId == cf.DomainId && i.AppDefinition!.Slug == "woocommerce");

        var domainName = cf.Domain!.DomainName;

        // Cache everything except the admin, login and (for WooCommerce) cart/checkout/account.
        await _cf.CreatePageRuleAsync(cf.Account!, cf.ZoneId, $"*{domainName}/wp-admin*",
            "{\"cache_level\":\"bypass\",\"security_level\":\"high\"}");
        await _cf.CreatePageRuleAsync(cf.Account!, cf.ZoneId, $"*{domainName}/wp-login.php*",
            "{\"cache_level\":\"bypass\",\"security_level\":\"high\"}");

        if (isWoo)
        {
            await _cf.CreatePageRuleAsync(cf.Account!, cf.ZoneId, $"*{domainName}/cart*",
                "{\"cache_level\":\"bypass\"}");
            await _cf.CreatePageRuleAsync(cf.Account!, cf.ZoneId, $"*{domainName}/checkout*",
                "{\"cache_level\":\"bypass\"}");
            await _cf.CreatePageRuleAsync(cf.Account!, cf.ZoneId, $"*{domainName}/my-account*",
                "{\"cache_level\":\"bypass\"}");
        }

        // Sensible defaults for WordPress: Full SSL, always HTTPS, HTML/CSS/JS minify, Brotli.
        await ApplyAsync(cf, (g, a, z) => g.SetSslModeAsync(a, z, CfSslMode.Full), c => c.SslMode = CfSslMode.Full);
        await ApplyAsync(cf, (g, a, z) => g.SetAlwaysHttpsAsync(a, z, true), c => c.AlwaysUseHttps = true);
        await ApplyAsync(cf, (g, a, z) => g.SetMinifyAsync(a, z, true, true, true),
            c => { c.MinifyCss = true; c.MinifyJs = true; c.MinifyHtml = true; });
        await ApplyAsync(cf, (g, a, z) => g.SetBrotliAsync(a, z, true), c => c.Brotli = true);

        // Persist the created page rules for the panel's Page Rules tab.
        var rules = new List<(string name, string url, string actions)>
        {
            ("Bypass cache for wp-admin", $"*{domainName}/wp-admin*", "cache_level=bypass, security_level=high"),
            ("Bypass cache for wp-login", $"*{domainName}/wp-login.php*", "cache_level=bypass, security_level=high")
        };
        if (isWoo)
        {
            rules.Add(("Bypass cache for cart", $"*{domainName}/cart*", "cache_level=bypass"));
            rules.Add(("Bypass cache for checkout", $"*{domainName}/checkout*", "cache_level=bypass"));
            rules.Add(("Bypass cache for account", $"*{domainName}/my-account*", "cache_level=bypass"));
        }

        foreach (var (name, url, actions) in rules)
        {
            _db.CloudflareRules.Add(new CloudflareRule
            {
                CloudflareDomainId = cf.Id,
                Type = CloudflareRuleType.PageRule,
                RuleId = "sim_pr_" + Guid.NewGuid().ToString("N")[..10],
                Name = name,
                Expression = url,
                Action = actions,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }
        await _db.SaveChangesAsync();

        await _notifications.NotifyAsync(userId, "WordPress optimisation applied",
            $"Cloudflare is now tuned for {(isWoo ? "WooCommerce" : "WordPress")} on {domainName}.", NotificationType.Success);
    }

    // ---------------- Helpers ----------------

    private static CloudflareZoneStatus ParseStatus(string status) => status switch
    {
        "active" => CloudflareZoneStatus.Active,
        "pending" => CloudflareZoneStatus.Pending,
        "paused" => CloudflareZoneStatus.Paused,
        "moved" => CloudflareZoneStatus.Moved,
        _ => CloudflareZoneStatus.Deactivated
    };

    private static string NormalizeHost(string name, string domainName)
    {
        var host = name.Replace($".{domainName}", "").Trim();
        if (host == domainName || host == "") return "@";
        return host;
    }
}
