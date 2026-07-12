using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

// ---------------- Account ----------------

/// <summary>A user's connection to a Cloudflare account, authenticated by a scoped API token.</summary>
public class CloudflareAccount
{
    public int Id { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [StringLength(200), EmailAddress] public string? Email { get; set; }

    /// <summary>Scoped API token. Stored as-is because every call needs it; treat as a secret.</summary>
    [Required] public string ApiToken { get; set; } = string.Empty;

    [StringLength(60)] public string AccountId { get; set; } = string.Empty;
    [StringLength(120)] public string? AccountName { get; set; }

    /// <summary>Token permission scopes reported at validation time (comma separated).</summary>
    [StringLength(400)] public string? TokenScopes { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastValidatedAt { get; set; }

    public ICollection<CloudflareDomain> Domains { get; set; } = new List<CloudflareDomain>();

    /// <summary>Masked token for display, e.g. "cf_live_…a1b2".</summary>
    public string MaskedToken =>
        ApiToken.Length <= 8 ? "••••" : $"{ApiToken[..4]}…{ApiToken[^4..]}";
}

// ---------------- Zone (domain) ----------------

public enum CloudflareZoneStatus
{
    Pending,
    Active,
    Paused,
    Moved,
    Deactivated
}

public enum CfSslMode
{
    Off,
    Flexible,
    Full,
    Strict
}

public enum CfSecurityLevel
{
    EssentiallyOff,
    Low,
    Medium,
    High,
    UnderAttack
}

public enum CfCacheLevel
{
    Bypass,
    Basic,
    Simplified,
    Aggressive
}

public enum CfPolish
{
    Off,
    Lossless,
    Lossy
}

/// <summary>
/// A Cloudflare zone linked to a panel domain. The *Settings columns are a cached snapshot
/// of the zone's Cloudflare configuration so the management tabs render without a round-trip;
/// each edit pushes to Cloudflare (or the simulated gateway) and updates the snapshot.
/// </summary>
public class CloudflareDomain
{
    public int Id { get; set; }

    public int DomainId { get; set; }
    public Domain? Domain { get; set; }

    public int CloudflareAccountId { get; set; }
    public CloudflareAccount? Account { get; set; }

    [StringLength(60)] public string ZoneId { get; set; } = string.Empty;

    public CloudflareZoneStatus Status { get; set; } = CloudflareZoneStatus.Pending;

    [StringLength(120)] public string? NameServer1 { get; set; }
    [StringLength(120)] public string? NameServer2 { get; set; }

    // Quick toggles
    public bool DevelopmentMode { get; set; }
    public bool UnderAttackMode { get; set; }
    public bool AlwaysUseHttps { get; set; } = true;

    // SSL / TLS
    public CfSslMode SslMode { get; set; } = CfSslMode.Full;
    [StringLength(10)] public string MinTlsVersion { get; set; } = "1.2";
    public bool Tls13 { get; set; } = true;
    public bool OpportunisticEncryption { get; set; } = true;

    // Speed
    public bool MinifyCss { get; set; }
    public bool MinifyJs { get; set; }
    public bool MinifyHtml { get; set; }
    public bool Brotli { get; set; } = true;
    public bool Http2 { get; set; } = true;
    public bool Http3 { get; set; } = true;
    public bool RocketLoader { get; set; }
    public bool EarlyHints { get; set; }
    public CfPolish Polish { get; set; } = CfPolish.Off;
    public bool WebpConversion { get; set; }

    // Caching
    public CfCacheLevel CacheLevel { get; set; } = CfCacheLevel.Aggressive;
    /// <summary>Browser cache TTL in seconds.</summary>
    public int BrowserCacheTtl { get; set; } = 14400;

    // Security
    public CfSecurityLevel SecurityLevel { get; set; } = CfSecurityLevel.Medium;
    public bool BotFightMode { get; set; }
    public int ChallengePassageTtl { get; set; } = 1800;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SyncedAt { get; set; }

    public ICollection<CloudflareRule> Rules { get; set; } = new List<CloudflareRule>();
}

// ---------------- Rules ----------------

public enum CloudflareRuleType
{
    PageRule,
    FirewallRule,
    RateLimit,
    Transform,
    IpAccessRule
}

public enum CfRuleAction
{
    Block,
    Challenge,
    JsChallenge,
    ManagedChallenge,
    Allow,
    Bypass,
    Log
}

public class CloudflareRule
{
    public int Id { get; set; }

    public int CloudflareDomainId { get; set; }
    public CloudflareDomain? CloudflareDomain { get; set; }

    public CloudflareRuleType Type { get; set; }

    /// <summary>The id Cloudflare assigned to the rule (or a simulated one).</summary>
    [StringLength(60)] public string RuleId { get; set; } = string.Empty;

    [Required, StringLength(160)] public string Name { get; set; } = string.Empty;

    /// <summary>Firewall expression, page-rule URL pattern, or IP/country target.</summary>
    [StringLength(1000)] public string Expression { get; set; } = string.Empty;

    /// <summary>Action for firewall/IP rules, or a JSON action list for page rules.</summary>
    [StringLength(1000)] public string Action { get; set; } = string.Empty;

    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Hit counter surfaced in the firewall tab (from analytics or simulated).</summary>
    public long Hits { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ---------------- Analytics ----------------

/// <summary>A daily analytics snapshot per zone, populated by the background sync service.</summary>
public class CloudflareAnalytics
{
    public int Id { get; set; }

    public int CloudflareDomainId { get; set; }
    public CloudflareDomain? CloudflareDomain { get; set; }

    public DateTime Date { get; set; }

    public long Requests { get; set; }
    public long Bandwidth { get; set; }
    public long Threats { get; set; }
    public long PageViews { get; set; }
    public long UniqueVisitors { get; set; }
    public long CachedRequests { get; set; }
    public long CachedBandwidth { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Percentage of requests served from cache.</summary>
    public double CacheHitRate => Requests == 0 ? 0 : Math.Round(CachedRequests * 100.0 / Requests, 1);
}

// ---------------- Cache purge log ----------------

public enum CfPurgeType
{
    Everything,
    Urls,
    Tags
}

public class CloudflareCache
{
    public int Id { get; set; }

    public int CloudflareDomainId { get; set; }
    public CloudflareDomain? CloudflareDomain { get; set; }

    public DateTime LastPurgedAt { get; set; } = DateTime.UtcNow;
    public CfPurgeType PurgeType { get; set; }

    /// <summary>What was purged: "all", the URL list, or the tag list.</summary>
    [StringLength(1000)] public string? Detail { get; set; }

    [StringLength(120)] public string? PurgedBy { get; set; }
}

// ---------------- Tunnel ----------------

public enum CfTunnelStatus
{
    Inactive,
    Healthy,
    Degraded,
    Down
}

/// <summary>A cloudflared tunnel that routes traffic to a local service without open inbound ports.</summary>
public class CloudflareTunnel
{
    public int Id { get; set; }

    public int CloudflareAccountId { get; set; }
    public CloudflareAccount? Account { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;

    [Required, StringLength(120)] public string Name { get; set; } = string.Empty;
    [StringLength(60)] public string TunnelId { get; set; } = string.Empty;

    /// <summary>Tunnel run token — shown once so the user can start cloudflared.</summary>
    public string? Secret { get; set; }

    public CfTunnelStatus Status { get; set; } = CfTunnelStatus.Inactive;

    /// <summary>Newline-separated "hostname => service" mappings, e.g. "app.example.com => http://localhost:8080".</summary>
    public string? Hostnames { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSeenAt { get; set; }

    public IEnumerable<(string Hostname, string Service)> HostnameList =>
        (Hostnames ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split("=>", 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2)
            .Select(parts => (parts[0], parts[1]));
}
