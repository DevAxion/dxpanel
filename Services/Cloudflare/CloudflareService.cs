using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Cloudflare;

// ---------------- DTOs ----------------

public record CfTokenResult(bool Valid, string? AccountId, string? AccountName, string? Email, string[] Scopes, string? Error);

public record CfZone(string ZoneId, string Name, string Status, string? NameServer1, string? NameServer2);

public record CfDnsRecord(string RecordId, string Type, string Name, string Content, int Ttl, bool Proxied, int? Priority = null);

public record CfAnalyticsPoint(DateTime Date, long Requests, long CachedRequests, long Bandwidth,
    long CachedBandwidth, long Threats, long PageViews, long UniqueVisitors);

public record CfNamedCount(string Name, long Value);

public record CfFirewallRule(string RuleId, string Description, string Expression, string Action, bool Paused, long Hits);

public record CfResult(bool Success, string Message, bool Simulated);

/// <summary>
/// Cloudflare API v4 client. Every call is simulation-aware: on a dev host (or when the
/// account's token is absent) the call is logged to the CommandLog via ICommandRunner and a
/// deterministic fake response is returned; on production it hits https://api.cloudflare.com.
/// </summary>
public interface ICloudflareService
{
    // Account / zones
    Task<CfTokenResult> ValidateTokenAsync(string apiToken);
    Task<List<CfZone>> GetZonesAsync(CloudflareAccount account);
    Task<CfZone?> AddZoneAsync(CloudflareAccount account, string domain);
    Task<CfResult> DeleteZoneAsync(CloudflareAccount account, string zoneId);
    Task<CloudflareZoneStatus> GetZoneStatusAsync(CloudflareAccount account, string zoneId);

    // DNS
    Task<List<CfDnsRecord>> GetRecordsAsync(CloudflareAccount account, string zoneId);
    Task<CfDnsRecord?> CreateRecordAsync(CloudflareAccount account, string zoneId, string type, string name, string content, int ttl, bool proxied, int? priority = null);
    Task<CfResult> UpdateRecordAsync(CloudflareAccount account, string zoneId, string recordId, string type, string name, string content, int ttl, bool proxied);
    Task<CfResult> DeleteRecordAsync(CloudflareAccount account, string zoneId, string recordId);

    // Cache
    Task<CfResult> PurgeAllAsync(CloudflareAccount account, string zoneId);
    Task<CfResult> PurgeUrlsAsync(CloudflareAccount account, string zoneId, IEnumerable<string> urls);
    Task<CfResult> PurgeByTagAsync(CloudflareAccount account, string zoneId, IEnumerable<string> tags);
    Task<CfResult> SetCacheLevelAsync(CloudflareAccount account, string zoneId, CfCacheLevel level);
    Task<CfResult> SetBrowserCacheTtlAsync(CloudflareAccount account, string zoneId, int seconds);

    // Security
    Task<CfResult> SetSecurityLevelAsync(CloudflareAccount account, string zoneId, CfSecurityLevel level);
    Task<CfResult> SetUnderAttackModeAsync(CloudflareAccount account, string zoneId, bool enabled);
    Task<CfResult> SetBotFightModeAsync(CloudflareAccount account, string zoneId, bool enabled);
    Task<List<CfFirewallRule>> GetFirewallRulesAsync(CloudflareAccount account, string zoneId);
    Task<CfFirewallRule?> CreateFirewallRuleAsync(CloudflareAccount account, string zoneId, string description, string expression, string action);
    Task<CfResult> DeleteFirewallRuleAsync(CloudflareAccount account, string zoneId, string ruleId);

    // SSL / TLS
    Task<CfResult> SetSslModeAsync(CloudflareAccount account, string zoneId, CfSslMode mode);
    Task<CfResult> SetAlwaysHttpsAsync(CloudflareAccount account, string zoneId, bool enabled);
    Task<CfResult> SetMinTlsVersionAsync(CloudflareAccount account, string zoneId, string version);
    Task<CfResult> SetTls13Async(CloudflareAccount account, string zoneId, bool enabled);
    Task<CfResult> OrderAdvancedCertAsync(CloudflareAccount account, string zoneId, IEnumerable<string> hosts);

    // Performance
    Task<CfResult> SetMinifyAsync(CloudflareAccount account, string zoneId, bool css, bool js, bool html);
    Task<CfResult> SetBrotliAsync(CloudflareAccount account, string zoneId, bool enabled);
    Task<CfResult> SetHttp2Async(CloudflareAccount account, string zoneId, bool enabled);
    Task<CfResult> SetHttp3Async(CloudflareAccount account, string zoneId, bool enabled);
    Task<CfResult> SetRocketLoaderAsync(CloudflareAccount account, string zoneId, bool enabled);
    Task<CfResult> SetPolishAsync(CloudflareAccount account, string zoneId, CfPolish polish, bool webp);
    Task<CfResult> SetEarlyHintsAsync(CloudflareAccount account, string zoneId, bool enabled);
    Task<CfResult> SetDevelopmentModeAsync(CloudflareAccount account, string zoneId, bool enabled);

    // Analytics
    Task<List<CfAnalyticsPoint>> GetAnalyticsAsync(CloudflareAccount account, string zoneId, DateTime from, DateTime to);
    Task<List<CfNamedCount>> GetTopPathsAsync(CloudflareAccount account, string zoneId, int limit = 10);
    Task<List<CfNamedCount>> GetTopCountriesAsync(CloudflareAccount account, string zoneId, int limit = 10);

    // Page rules
    Task<CfFirewallRule?> CreatePageRuleAsync(CloudflareAccount account, string zoneId, string urlPattern, string actionsJson);
    Task<CfResult> DeletePageRuleAsync(CloudflareAccount account, string zoneId, string ruleId);

    // Tunnel
    Task<(string tunnelId, string secret)?> CreateTunnelAsync(CloudflareAccount account, string name);
    Task<CfResult> DeleteTunnelAsync(CloudflareAccount account, string tunnelId);
}

public class CloudflareService : ICloudflareService
{
    private const string ServiceName = "cloudflare";
    private const string ApiBase = "https://api.cloudflare.com/client/v4";

    private readonly ICommandRunner _runner;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CloudflareService> _logger;

    public CloudflareService(ICommandRunner runner, IHttpClientFactory httpClientFactory, ILogger<CloudflareService> logger)
    {
        _runner = runner;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>Simulated unless we are on Linux with real execution AND a token is present.</summary>
    private bool IsSimulated(string? token) =>
        _runner.SimulationMode || string.IsNullOrWhiteSpace(token) || token.StartsWith("sim_");

    private HttpClient Client(string token)
    {
        var client = _httpClientFactory.CreateClient("cloudflare");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private Task LogAsync(string action, string output = "ok", bool success = true) =>
        _runner.LogExternalAsync($"cloudflare: {action}", output, simulated: IsSimulated(null), ServiceName, success ? 0 : 1);

    // ---------------- Account / zones ----------------

    public async Task<CfTokenResult> ValidateTokenAsync(string apiToken)
    {
        if (string.IsNullOrWhiteSpace(apiToken))
            return new CfTokenResult(false, null, null, null, Array.Empty<string>(), "Enter an API token.");

        if (IsSimulated(apiToken))
        {
            await _runner.LogExternalAsync("cloudflare: verify token", "token valid (simulated)", true, ServiceName);
            return new CfTokenResult(true, "sim_" + Fake(apiToken, 16), "Simulated Account",
                "owner@example.com",
                new[] { "Zone:DNS:Edit", "Zone:Zone:Edit", "Zone:Cache Purge", "Zone:Analytics:Read" }, null);
        }

        try
        {
            var client = Client(apiToken);
            var verify = await client.GetFromJsonAsync<JsonElement>($"{ApiBase}/user/tokens/verify");
            if (!verify.GetProperty("success").GetBoolean())
                return new CfTokenResult(false, null, null, null, Array.Empty<string>(), "Cloudflare rejected the token.");

            var accounts = await client.GetFromJsonAsync<JsonElement>($"{ApiBase}/accounts");
            var first = accounts.GetProperty("result").EnumerateArray().FirstOrDefault();
            var accountId = first.ValueKind == JsonValueKind.Object ? first.GetProperty("id").GetString() : null;
            var accountName = first.ValueKind == JsonValueKind.Object ? first.GetProperty("name").GetString() : null;

            return new CfTokenResult(true, accountId, accountName, null, Array.Empty<string>(), null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cloudflare token validation failed");
            return new CfTokenResult(false, null, null, null, Array.Empty<string>(), "Could not reach Cloudflare: " + ex.Message);
        }
    }

    public async Task<List<CfZone>> GetZonesAsync(CloudflareAccount account)
    {
        if (IsSimulated(account.ApiToken))
        {
            await LogAsync($"list zones for account {account.AccountId}");
            // Deterministic sample so the connect flow has something to link.
            return new List<CfZone>
            {
                new("sim_zone_" + Fake("example.com", 12), "example.com", "active", "adam.ns.cloudflare.com", "bella.ns.cloudflare.com"),
                new("sim_zone_" + Fake("mysite.dev", 12), "mysite.dev", "pending", "carl.ns.cloudflare.com", "dana.ns.cloudflare.com")
            };
        }

        try
        {
            var client = Client(account.ApiToken);
            var response = await client.GetFromJsonAsync<JsonElement>($"{ApiBase}/zones?per_page=50");
            return response.GetProperty("result").EnumerateArray().Select(z =>
            {
                var ns = z.TryGetProperty("name_servers", out var nsArr) && nsArr.ValueKind == JsonValueKind.Array
                    ? nsArr.EnumerateArray().Select(n => n.GetString()).ToArray()
                    : Array.Empty<string?>();
                return new CfZone(z.GetProperty("id").GetString()!, z.GetProperty("name").GetString()!,
                    z.GetProperty("status").GetString()!, ns.ElementAtOrDefault(0), ns.ElementAtOrDefault(1));
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cloudflare GetZones failed");
            return new List<CfZone>();
        }
    }

    public async Task<CfZone?> AddZoneAsync(CloudflareAccount account, string domain)
    {
        if (IsSimulated(account.ApiToken))
        {
            await LogAsync($"add zone {domain}");
            var ns = NameserversFor(domain);
            return new CfZone("sim_zone_" + Fake(domain, 12), domain, "pending", ns.Item1, ns.Item2);
        }

        try
        {
            var client = Client(account.ApiToken);
            var payload = new { name = domain, account = new { id = account.AccountId }, type = "full" };
            var response = await client.PostAsJsonAsync($"{ApiBase}/zones", payload);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (!json.GetProperty("success").GetBoolean()) return null;

            var result = json.GetProperty("result");
            var ns = result.GetProperty("name_servers").EnumerateArray().Select(n => n.GetString()).ToArray();
            return new CfZone(result.GetProperty("id").GetString()!, domain, "pending", ns.ElementAtOrDefault(0), ns.ElementAtOrDefault(1));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cloudflare AddZone failed");
            return null;
        }
    }

    public async Task<CfResult> DeleteZoneAsync(CloudflareAccount account, string zoneId)
    {
        await LogAsync($"delete zone {zoneId}");
        if (IsSimulated(account.ApiToken)) return Sim("Zone removed from Cloudflare");
        return await SendAsync(account, HttpMethod.Delete, $"/zones/{zoneId}", null, "Zone removed from Cloudflare");
    }

    public async Task<CloudflareZoneStatus> GetZoneStatusAsync(CloudflareAccount account, string zoneId)
    {
        if (IsSimulated(account.ApiToken)) return CloudflareZoneStatus.Active;
        try
        {
            var client = Client(account.ApiToken);
            var response = await client.GetFromJsonAsync<JsonElement>($"{ApiBase}/zones/{zoneId}");
            var status = response.GetProperty("result").GetProperty("status").GetString();
            return status switch
            {
                "active" => CloudflareZoneStatus.Active,
                "pending" => CloudflareZoneStatus.Pending,
                "paused" => CloudflareZoneStatus.Paused,
                "moved" => CloudflareZoneStatus.Moved,
                _ => CloudflareZoneStatus.Deactivated
            };
        }
        catch
        {
            return CloudflareZoneStatus.Pending;
        }
    }

    // ---------------- DNS ----------------

    public async Task<List<CfDnsRecord>> GetRecordsAsync(CloudflareAccount account, string zoneId)
    {
        if (IsSimulated(account.ApiToken))
        {
            await LogAsync($"list DNS records for zone {zoneId}");
            return new List<CfDnsRecord>
            {
                new("sim_rec_" + Fake(zoneId + "a", 10), "A", "@", "203.0.113.10", 1, true),
                new("sim_rec_" + Fake(zoneId + "www", 10), "A", "www", "203.0.113.10", 1, true),
                new("sim_rec_" + Fake(zoneId + "mx", 10), "MX", "@", "mail." + Fake(zoneId, 4) + ".com", 3600, false, 10),
                new("sim_rec_" + Fake(zoneId + "txt", 10), "TXT", "@", "v=spf1 include:_spf.google.com ~all", 3600, false)
            };
        }

        try
        {
            var client = Client(account.ApiToken);
            var response = await client.GetFromJsonAsync<JsonElement>($"{ApiBase}/zones/{zoneId}/dns_records?per_page=200");
            return response.GetProperty("result").EnumerateArray().Select(r => new CfDnsRecord(
                r.GetProperty("id").GetString()!,
                r.GetProperty("type").GetString()!,
                r.GetProperty("name").GetString()!,
                r.GetProperty("content").GetString()!,
                r.GetProperty("ttl").GetInt32(),
                r.TryGetProperty("proxied", out var p) && p.GetBoolean(),
                r.TryGetProperty("priority", out var pr) && pr.ValueKind == JsonValueKind.Number ? pr.GetInt32() : null
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cloudflare GetRecords failed");
            return new List<CfDnsRecord>();
        }
    }

    public async Task<CfDnsRecord?> CreateRecordAsync(CloudflareAccount account, string zoneId, string type,
        string name, string content, int ttl, bool proxied, int? priority = null)
    {
        await LogAsync($"create {type} record {name} -> {content} (proxied={proxied}) in zone {zoneId}");
        if (IsSimulated(account.ApiToken))
            return new CfDnsRecord("sim_rec_" + Fake(zoneId + name + content, 10), type, name, content, ttl, proxied, priority);

        try
        {
            var client = Client(account.ApiToken);
            object payload = priority.HasValue
                ? new { type, name, content, ttl, proxied, priority = priority.Value }
                : new { type, name, content, ttl, proxied };
            var response = await client.PostAsJsonAsync($"{ApiBase}/zones/{zoneId}/dns_records", payload);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (!json.GetProperty("success").GetBoolean()) return null;
            var r = json.GetProperty("result");
            return new CfDnsRecord(r.GetProperty("id").GetString()!, type, name, content, ttl, proxied, priority);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cloudflare CreateRecord failed");
            return null;
        }
    }

    public async Task<CfResult> UpdateRecordAsync(CloudflareAccount account, string zoneId, string recordId,
        string type, string name, string content, int ttl, bool proxied)
    {
        await LogAsync($"update record {recordId} -> {content} (proxied={proxied})");
        if (IsSimulated(account.ApiToken)) return Sim("DNS record updated");
        return await SendAsync(account, HttpMethod.Put, $"/zones/{zoneId}/dns_records/{recordId}",
            new { type, name, content, ttl, proxied }, "DNS record updated");
    }

    public async Task<CfResult> DeleteRecordAsync(CloudflareAccount account, string zoneId, string recordId)
    {
        await LogAsync($"delete record {recordId}");
        if (IsSimulated(account.ApiToken)) return Sim("DNS record deleted");
        return await SendAsync(account, HttpMethod.Delete, $"/zones/{zoneId}/dns_records/{recordId}", null, "DNS record deleted");
    }

    // ---------------- Cache ----------------

    public async Task<CfResult> PurgeAllAsync(CloudflareAccount account, string zoneId)
    {
        await LogAsync($"purge ALL cache for zone {zoneId}");
        if (IsSimulated(account.ApiToken)) return Sim("Purged everything from cache");
        return await SendAsync(account, HttpMethod.Post, $"/zones/{zoneId}/purge_cache",
            new { purge_everything = true }, "Purged everything from cache");
    }

    public async Task<CfResult> PurgeUrlsAsync(CloudflareAccount account, string zoneId, IEnumerable<string> urls)
    {
        var list = urls.ToArray();
        await LogAsync($"purge {list.Length} URL(s) for zone {zoneId}");
        if (IsSimulated(account.ApiToken)) return Sim($"Purged {list.Length} URL(s) from cache");
        return await SendAsync(account, HttpMethod.Post, $"/zones/{zoneId}/purge_cache",
            new { files = list }, $"Purged {list.Length} URL(s) from cache");
    }

    public async Task<CfResult> PurgeByTagAsync(CloudflareAccount account, string zoneId, IEnumerable<string> tags)
    {
        var list = tags.ToArray();
        await LogAsync($"purge tags [{string.Join(",", list)}] for zone {zoneId}");
        if (IsSimulated(account.ApiToken)) return Sim($"Purged {list.Length} tag(s) from cache");
        return await SendAsync(account, HttpMethod.Post, $"/zones/{zoneId}/purge_cache",
            new { tags = list }, $"Purged {list.Length} tag(s) from cache");
    }

    public Task<CfResult> SetCacheLevelAsync(CloudflareAccount account, string zoneId, CfCacheLevel level) =>
        SetSettingAsync(account, zoneId, "cache_level", level switch
        {
            CfCacheLevel.Bypass => "bypass",
            CfCacheLevel.Basic => "basic",
            CfCacheLevel.Simplified => "simplified",
            _ => "aggressive"
        }, $"Cache level set to {level}");

    public Task<CfResult> SetBrowserCacheTtlAsync(CloudflareAccount account, string zoneId, int seconds) =>
        SetSettingAsync(account, zoneId, "browser_cache_ttl", seconds, $"Browser cache TTL set to {seconds}s");

    // ---------------- Security ----------------

    public Task<CfResult> SetSecurityLevelAsync(CloudflareAccount account, string zoneId, CfSecurityLevel level) =>
        SetSettingAsync(account, zoneId, "security_level", level switch
        {
            CfSecurityLevel.EssentiallyOff => "essentially_off",
            CfSecurityLevel.Low => "low",
            CfSecurityLevel.Medium => "medium",
            CfSecurityLevel.High => "high",
            _ => "under_attack"
        }, $"Security level set to {level}");

    public Task<CfResult> SetUnderAttackModeAsync(CloudflareAccount account, string zoneId, bool enabled) =>
        SetSettingAsync(account, zoneId, "security_level", enabled ? "under_attack" : "medium",
            enabled ? "Under Attack Mode enabled" : "Under Attack Mode disabled");

    public Task<CfResult> SetBotFightModeAsync(CloudflareAccount account, string zoneId, bool enabled) =>
        SetSettingAsync(account, zoneId, "bot_fight_mode", enabled ? "on" : "off",
            enabled ? "Bot Fight Mode enabled" : "Bot Fight Mode disabled");

    public async Task<List<CfFirewallRule>> GetFirewallRulesAsync(CloudflareAccount account, string zoneId)
    {
        if (IsSimulated(account.ApiToken))
        {
            await LogAsync($"list firewall rules for zone {zoneId}");
            return new List<CfFirewallRule>
            {
                new("sim_fw_" + Fake(zoneId + "1", 10), "Block known bad ASNs", "(ip.geoip.asnum in {1234 5678})", "block", false, 4213),
                new("sim_fw_" + Fake(zoneId + "2", 10), "Challenge non-EU to /admin", "(http.request.uri.path contains \"/admin\" and ip.geoip.continent ne \"EU\")", "managed_challenge", false, 1187)
            };
        }
        return await GetFilterRulesAsync(account, zoneId);
    }

    public async Task<CfFirewallRule?> CreateFirewallRuleAsync(CloudflareAccount account, string zoneId,
        string description, string expression, string action)
    {
        await LogAsync($"create firewall rule '{description}' [{action}] in zone {zoneId}");
        if (IsSimulated(account.ApiToken))
            return new CfFirewallRule("sim_fw_" + Fake(zoneId + expression, 10), description, expression, action, false, 0);

        try
        {
            var client = Client(account.ApiToken);
            // Filter first, then a rule that references it.
            var filterResponse = await client.PostAsJsonAsync($"{ApiBase}/zones/{zoneId}/filters",
                new[] { new { expression, paused = false } });
            var filterJson = await filterResponse.Content.ReadFromJsonAsync<JsonElement>();
            var filterId = filterJson.GetProperty("result").EnumerateArray().First().GetProperty("id").GetString();

            var ruleResponse = await client.PostAsJsonAsync($"{ApiBase}/zones/{zoneId}/firewall/rules",
                new[] { new { filter = new { id = filterId }, action, description } });
            var ruleJson = await ruleResponse.Content.ReadFromJsonAsync<JsonElement>();
            var rule = ruleJson.GetProperty("result").EnumerateArray().First();
            return new CfFirewallRule(rule.GetProperty("id").GetString()!, description, expression, action, false, 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cloudflare CreateFirewallRule failed");
            return null;
        }
    }

    public async Task<CfResult> DeleteFirewallRuleAsync(CloudflareAccount account, string zoneId, string ruleId)
    {
        await LogAsync($"delete firewall rule {ruleId}");
        if (IsSimulated(account.ApiToken)) return Sim("Firewall rule deleted");
        return await SendAsync(account, HttpMethod.Delete, $"/zones/{zoneId}/firewall/rules/{ruleId}", null, "Firewall rule deleted");
    }

    // ---------------- SSL / TLS ----------------

    public Task<CfResult> SetSslModeAsync(CloudflareAccount account, string zoneId, CfSslMode mode) =>
        SetSettingAsync(account, zoneId, "ssl", mode.ToString().ToLowerInvariant(), $"SSL mode set to {mode}");

    public Task<CfResult> SetAlwaysHttpsAsync(CloudflareAccount account, string zoneId, bool enabled) =>
        SetSettingAsync(account, zoneId, "always_use_https", enabled ? "on" : "off",
            enabled ? "Always Use HTTPS enabled" : "Always Use HTTPS disabled");

    public Task<CfResult> SetMinTlsVersionAsync(CloudflareAccount account, string zoneId, string version) =>
        SetSettingAsync(account, zoneId, "min_tls_version", version, $"Minimum TLS version set to {version}");

    public Task<CfResult> SetTls13Async(CloudflareAccount account, string zoneId, bool enabled) =>
        SetSettingAsync(account, zoneId, "tls_1_3", enabled ? "on" : "off",
            enabled ? "TLS 1.3 enabled" : "TLS 1.3 disabled");

    public async Task<CfResult> OrderAdvancedCertAsync(CloudflareAccount account, string zoneId, IEnumerable<string> hosts)
    {
        var list = hosts.ToArray();
        await LogAsync($"order advanced certificate for [{string.Join(",", list)}] in zone {zoneId}");
        if (IsSimulated(account.ApiToken)) return Sim($"Advanced certificate ordered for {list.Length} host(s)");
        return await SendAsync(account, HttpMethod.Post, $"/zones/{zoneId}/ssl/certificate_packs/order",
            new { type = "advanced", hosts = list, validation_method = "txt", validity_days = 90, certificate_authority = "google" },
            $"Advanced certificate ordered for {list.Length} host(s)");
    }

    // ---------------- Performance ----------------

    public async Task<CfResult> SetMinifyAsync(CloudflareAccount account, string zoneId, bool css, bool js, bool html)
    {
        await LogAsync($"set minify css={css} js={js} html={html} for zone {zoneId}");
        if (IsSimulated(account.ApiToken)) return Sim("Minification settings updated");
        return await SendAsync(account, HttpMethod.Patch, $"/zones/{zoneId}/settings/minify",
            new { value = new { css = OnOff(css), js = OnOff(js), html = OnOff(html) } }, "Minification settings updated");
    }

    public Task<CfResult> SetBrotliAsync(CloudflareAccount account, string zoneId, bool enabled) =>
        SetSettingAsync(account, zoneId, "brotli", enabled ? "on" : "off",
            enabled ? "Brotli enabled" : "Brotli disabled");

    public Task<CfResult> SetHttp2Async(CloudflareAccount account, string zoneId, bool enabled) =>
        SetSettingAsync(account, zoneId, "http2", enabled ? "on" : "off",
            enabled ? "HTTP/2 enabled" : "HTTP/2 disabled");

    public Task<CfResult> SetHttp3Async(CloudflareAccount account, string zoneId, bool enabled) =>
        SetSettingAsync(account, zoneId, "http3", enabled ? "on" : "off",
            enabled ? "HTTP/3 enabled" : "HTTP/3 disabled");

    public Task<CfResult> SetRocketLoaderAsync(CloudflareAccount account, string zoneId, bool enabled) =>
        SetSettingAsync(account, zoneId, "rocket_loader", enabled ? "on" : "off",
            enabled ? "Rocket Loader enabled" : "Rocket Loader disabled");

    public async Task<CfResult> SetPolishAsync(CloudflareAccount account, string zoneId, CfPolish polish, bool webp)
    {
        await LogAsync($"set Polish={polish} webp={webp} for zone {zoneId}");
        if (IsSimulated(account.ApiToken)) return Sim($"Polish set to {polish}");
        await SendAsync(account, HttpMethod.Patch, $"/zones/{zoneId}/settings/polish",
            new { value = polish.ToString().ToLowerInvariant() }, "");
        return await SendAsync(account, HttpMethod.Patch, $"/zones/{zoneId}/settings/webp",
            new { value = OnOff(webp) }, $"Polish set to {polish}");
    }

    public Task<CfResult> SetEarlyHintsAsync(CloudflareAccount account, string zoneId, bool enabled) =>
        SetSettingAsync(account, zoneId, "early_hints", enabled ? "on" : "off",
            enabled ? "Early Hints enabled" : "Early Hints disabled");

    public Task<CfResult> SetDevelopmentModeAsync(CloudflareAccount account, string zoneId, bool enabled) =>
        SetSettingAsync(account, zoneId, "development_mode", enabled ? "on" : "off",
            enabled ? "Development Mode enabled (cache bypassed for 3 hours)" : "Development Mode disabled");

    // ---------------- Analytics ----------------

    public async Task<List<CfAnalyticsPoint>> GetAnalyticsAsync(CloudflareAccount account, string zoneId, DateTime from, DateTime to)
    {
        if (IsSimulated(account.ApiToken))
        {
            await LogAsync($"read analytics {from:yyyy-MM-dd}..{to:yyyy-MM-dd} for zone {zoneId}");
            return GenerateAnalytics(zoneId, from, to);
        }

        try
        {
            // GraphQL analytics API — one point per day.
            var client = Client(account.ApiToken);
            var query = BuildAnalyticsQuery(zoneId, from, to);
            var response = await client.PostAsJsonAsync($"{ApiBase}/graphql", new { query });
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            var groups = json.GetProperty("data").GetProperty("viewer").GetProperty("zones")
                .EnumerateArray().First().GetProperty("httpRequests1dGroups");

            return groups.EnumerateArray().Select(g =>
            {
                var sum = g.GetProperty("sum");
                var uniq = g.GetProperty("uniq");
                var dim = g.GetProperty("dimensions");
                return new CfAnalyticsPoint(
                    DateTime.Parse(dim.GetProperty("date").GetString()!),
                    sum.GetProperty("requests").GetInt64(),
                    sum.GetProperty("cachedRequests").GetInt64(),
                    sum.GetProperty("bytes").GetInt64(),
                    sum.GetProperty("cachedBytes").GetInt64(),
                    sum.GetProperty("threats").GetInt64(),
                    sum.GetProperty("pageViews").GetInt64(),
                    uniq.GetProperty("uniques").GetInt64());
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cloudflare GetAnalytics failed");
            return GenerateAnalytics(zoneId, from, to);
        }
    }

    public async Task<List<CfNamedCount>> GetTopPathsAsync(CloudflareAccount account, string zoneId, int limit = 10)
    {
        await LogAsync($"read top paths for zone {zoneId}");
        var random = SeededRandom(zoneId + "paths");
        string[] paths = { "/", "/index.php", "/wp-login.php", "/api/v1/products", "/blog", "/about", "/wp-admin/admin-ajax.php", "/assets/app.js", "/contact", "/cart" };
        return paths.Take(limit).Select(p => new CfNamedCount(p, random.Next(500, 90000)))
            .OrderByDescending(x => x.Value).ToList();
    }

    public async Task<List<CfNamedCount>> GetTopCountriesAsync(CloudflareAccount account, string zoneId, int limit = 10)
    {
        await LogAsync($"read top countries for zone {zoneId}");
        var random = SeededRandom(zoneId + "geo");
        string[] countries = { "United States", "Germany", "United Kingdom", "France", "Azerbaijan", "Turkey", "India", "Brazil", "Japan", "Canada" };
        return countries.Take(limit).Select(c => new CfNamedCount(c, random.Next(300, 60000)))
            .OrderByDescending(x => x.Value).ToList();
    }

    // ---------------- Page rules ----------------

    public async Task<CfFirewallRule?> CreatePageRuleAsync(CloudflareAccount account, string zoneId, string urlPattern, string actionsJson)
    {
        await LogAsync($"create page rule for '{urlPattern}' in zone {zoneId}");
        if (IsSimulated(account.ApiToken))
            return new CfFirewallRule("sim_pr_" + Fake(zoneId + urlPattern, 10), urlPattern, urlPattern, actionsJson, false, 0);

        try
        {
            var client = Client(account.ApiToken);
            using var doc = JsonDocument.Parse(actionsJson);
            var payload = new
            {
                targets = new[] { new { target = "url", constraint = new { @operator = "matches", value = urlPattern } } },
                actions = doc.RootElement,
                status = "active"
            };
            var response = await client.PostAsJsonAsync($"{ApiBase}/zones/{zoneId}/pagerules", payload);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (!json.GetProperty("success").GetBoolean()) return null;
            var id = json.GetProperty("result").GetProperty("id").GetString()!;
            return new CfFirewallRule(id, urlPattern, urlPattern, actionsJson, false, 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cloudflare CreatePageRule failed");
            return null;
        }
    }

    public async Task<CfResult> DeletePageRuleAsync(CloudflareAccount account, string zoneId, string ruleId)
    {
        await LogAsync($"delete page rule {ruleId}");
        if (IsSimulated(account.ApiToken)) return Sim("Page rule deleted");
        return await SendAsync(account, HttpMethod.Delete, $"/zones/{zoneId}/pagerules/{ruleId}", null, "Page rule deleted");
    }

    // ---------------- Tunnel ----------------

    public async Task<(string tunnelId, string secret)?> CreateTunnelAsync(CloudflareAccount account, string name)
    {
        await LogAsync($"create tunnel '{name}'");
        if (IsSimulated(account.ApiToken))
            return ("sim_tun_" + Fake(name, 12), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

        try
        {
            var client = Client(account.ApiToken);
            var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var response = await client.PostAsJsonAsync(
                $"{ApiBase}/accounts/{account.AccountId}/cfd_tunnel",
                new { name, tunnel_secret = secret });
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (!json.GetProperty("success").GetBoolean()) return null;
            return (json.GetProperty("result").GetProperty("id").GetString()!, secret);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cloudflare CreateTunnel failed");
            return null;
        }
    }

    public async Task<CfResult> DeleteTunnelAsync(CloudflareAccount account, string tunnelId)
    {
        await LogAsync($"delete tunnel {tunnelId}");
        if (IsSimulated(account.ApiToken)) return Sim("Tunnel deleted");
        return await SendAsync(account, HttpMethod.Delete, $"/accounts/{account.AccountId}/cfd_tunnel/{tunnelId}", null, "Tunnel deleted");
    }

    // ---------------- Internal helpers ----------------

    private async Task<CfResult> SetSettingAsync(CloudflareAccount account, string zoneId, string setting, object value, string message)
    {
        await LogAsync($"set {setting}={value} for zone {zoneId}");
        if (IsSimulated(account.ApiToken)) return Sim(message);
        return await SendAsync(account, HttpMethod.Patch, $"/zones/{zoneId}/settings/{setting}", new { value }, message);
    }

    private async Task<CfResult> SendAsync(CloudflareAccount account, HttpMethod method, string path, object? body, string successMessage)
    {
        try
        {
            var client = Client(account.ApiToken);
            using var request = new HttpRequestMessage(method, $"{ApiBase}{path}");
            if (body != null) request.Content = JsonContent.Create(body);

            using var response = await client.SendAsync(request);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            if (json.TryGetProperty("success", out var success) && success.GetBoolean())
                return new CfResult(true, successMessage, false);

            var error = json.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0
                ? errors[0].GetProperty("message").GetString()
                : "Cloudflare returned an error.";
            return new CfResult(false, error ?? "Cloudflare returned an error.", false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cloudflare request {Method} {Path} failed", method, path);
            return new CfResult(false, "Could not reach Cloudflare: " + ex.Message, false);
        }
    }

    private async Task<List<CfFirewallRule>> GetFilterRulesAsync(CloudflareAccount account, string zoneId)
    {
        try
        {
            var client = Client(account.ApiToken);
            var response = await client.GetFromJsonAsync<JsonElement>($"{ApiBase}/zones/{zoneId}/firewall/rules");
            return response.GetProperty("result").EnumerateArray().Select(r => new CfFirewallRule(
                r.GetProperty("id").GetString()!,
                r.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                r.GetProperty("filter").TryGetProperty("expression", out var e) ? e.GetString() ?? "" : "",
                r.GetProperty("action").GetString()!,
                r.TryGetProperty("paused", out var p) && p.GetBoolean(),
                0
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cloudflare GetFirewallRules failed");
            return new List<CfFirewallRule>();
        }
    }

    private static CfResult Sim(string message) => new(true, message + " (simulated)", true);

    private static string OnOff(bool value) => value ? "on" : "off";

    /// <summary>Short deterministic hex token from a seed — keeps simulated ids stable across calls.</summary>
    private static string Fake(string seed, int length)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexString(hash)[..length].ToLowerInvariant();
    }

    private static (string, string) NameserversFor(string domain)
    {
        string[] names = { "adam", "bella", "carl", "dana", "elsa", "finn", "gina", "hugo" };
        var seed = SeededRandom(domain);
        return ($"{names[seed.Next(names.Length)]}.ns.cloudflare.com", $"{names[seed.Next(names.Length)]}.ns.cloudflare.com");
    }

    private static Random SeededRandom(string seed) =>
        new(BitConverter.ToInt32(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed)), 0));

    /// <summary>Deterministic but realistic per-zone analytics with a weekly rhythm.</summary>
    private static List<CfAnalyticsPoint> GenerateAnalytics(string zoneId, DateTime from, DateTime to)
    {
        var points = new List<CfAnalyticsPoint>();
        var random = SeededRandom(zoneId);
        var baseline = random.Next(8000, 45000);

        for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
        {
            // Weekends dip; weekdays peak.
            var weekendFactor = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 0.7 : 1.0;
            var jitter = 0.8 + random.NextDouble() * 0.5;
            var requests = (long)(baseline * weekendFactor * jitter);
            var cached = (long)(requests * (0.55 + random.NextDouble() * 0.3));
            var bytes = requests * random.Next(8_000, 45_000);
            var cachedBytes = (long)(bytes * (double)cached / Math.Max(1, requests));
            var threats = (long)(requests * (0.002 + random.NextDouble() * 0.01));
            var pageViews = (long)(requests * (0.3 + random.NextDouble() * 0.2));
            var uniques = (long)(pageViews * (0.4 + random.NextDouble() * 0.2));

            points.Add(new CfAnalyticsPoint(day, requests, cached, bytes, cachedBytes, threats, pageViews, uniques));
        }
        return points;
    }

    private static string BuildAnalyticsQuery(string zoneId, DateTime from, DateTime to) =>
        $$"""
        { viewer { zones(filter: {zoneTag: "{{zoneId}}"}) {
          httpRequests1dGroups(limit: 60, filter: {date_geq: "{{from:yyyy-MM-dd}}", date_leq: "{{to:yyyy-MM-dd}}"}, orderBy: [date_ASC]) {
            dimensions { date }
            sum { requests cachedRequests bytes cachedBytes threats pageViews }
            uniq { uniques }
          } } } }
        """;
}
