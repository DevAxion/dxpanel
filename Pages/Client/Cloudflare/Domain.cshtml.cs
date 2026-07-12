using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Cloudflare;

namespace SRXPanel.Pages.Client.Cloudflare;

public class DomainModel : PageModel
{
    private readonly ICloudflareManager _cf;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _auditLog;

    public DomainModel(ICloudflareManager cf, ApplicationDbContext db,
        UserManager<ApplicationUser> userManager, IAuditLogService auditLog)
    {
        _cf = cf;
        _db = db;
        _userManager = userManager;
        _auditLog = auditLog;
    }

    [BindProperty(SupportsGet = true)] public int DomainId { get; set; }
    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "overview";

    public CloudflareDomain Cf { get; private set; } = null!;
    public bool IsWordPress { get; private set; }

    // Tab data (loaded on demand)
    public List<CfDnsRecord> Records { get; private set; } = new();
    public List<CfFirewallRule> FirewallRules { get; private set; } = new();
    public List<CloudflareRule> PageRules { get; private set; } = new();
    public List<CloudflareAnalytics> Analytics { get; private set; } = new();
    public List<CfNamedCount> TopPaths { get; private set; } = new();
    public List<CfNamedCount> TopCountries { get; private set; } = new();
    public List<CloudflareCache> RecentPurges { get; private set; } = new();
    public CloudflareAnalytics? Today { get; private set; }

    private string? _userId;

    private async Task<bool> LoadAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return false;
        _userId = user.Id;

        var cf = await _cf.GetLinkedDomainAsync(user.Id, DomainId);
        if (cf == null) return false;

        Cf = cf;
        IsWordPress = await _cf.IsWordPressAsync(DomainId);
        return true;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync()) return NotFound();

        switch (Tab)
        {
            case "dns":
                Records = await _cf.GetRecordsAsync(Cf);
                break;
            case "firewall":
                FirewallRules = await _cf.Gateway.GetFirewallRulesAsync(Cf.Account!, Cf.ZoneId);
                break;
            case "pagerules":
                PageRules = await _db.CloudflareRules
                    .Where(r => r.CloudflareDomainId == Cf.Id && r.Type == CloudflareRuleType.PageRule)
                    .OrderBy(r => r.Priority).ThenBy(r => r.Id).ToListAsync();
                break;
            case "analytics":
                await LoadAnalyticsAsync();
                break;
            case "caching":
                RecentPurges = await _db.CloudflareCaches
                    .Where(c => c.CloudflareDomainId == Cf.Id)
                    .OrderByDescending(c => c.LastPurgedAt).Take(10).ToListAsync();
                await LoadTodayAsync();
                break;
            default:
                await LoadTodayAsync();
                break;
        }

        return Page();
    }

    private async Task LoadTodayAsync()
    {
        var from = DateTime.UtcNow.Date.AddDays(-1);
        var to = DateTime.UtcNow.Date;
        var points = await _cf.Gateway.GetAnalyticsAsync(Cf.Account!, Cf.ZoneId, from, to);
        Today = points.OrderByDescending(p => p.Date).Select(p => new CloudflareAnalytics
        {
            Requests = p.Requests, CachedRequests = p.CachedRequests, Bandwidth = p.Bandwidth,
            Threats = p.Threats, PageViews = p.PageViews, UniqueVisitors = p.UniqueVisitors
        }).FirstOrDefault();
    }

    private async Task LoadAnalyticsAsync()
    {
        var from = DateTime.UtcNow.Date.AddDays(-13);
        var to = DateTime.UtcNow.Date;
        var points = await _cf.Gateway.GetAnalyticsAsync(Cf.Account!, Cf.ZoneId, from, to);

        Analytics = points.Select(p => new CloudflareAnalytics
        {
            Date = p.Date, Requests = p.Requests, CachedRequests = p.CachedRequests,
            Bandwidth = p.Bandwidth, CachedBandwidth = p.CachedBandwidth, Threats = p.Threats,
            PageViews = p.PageViews, UniqueVisitors = p.UniqueVisitors
        }).ToList();

        TopPaths = await _cf.Gateway.GetTopPathsAsync(Cf.Account!, Cf.ZoneId, 10);
        TopCountries = await _cf.Gateway.GetTopCountriesAsync(Cf.Account!, Cf.ZoneId, 10);
    }

    // ---------------- Overview toggles ----------------

    public async Task<IActionResult> OnPostToggleAsync(string setting, bool value)
    {
        if (!await LoadAsync()) return NotFound();

        var result = setting switch
        {
            "dev" => await _cf.ApplyAsync(Cf, (g, a, z) => g.SetDevelopmentModeAsync(a, z, value), c => c.DevelopmentMode = value),
            "attack" => await _cf.ApplyAsync(Cf, (g, a, z) => g.SetUnderAttackModeAsync(a, z, value),
                c => { c.UnderAttackMode = value; c.SecurityLevel = value ? CfSecurityLevel.UnderAttack : CfSecurityLevel.Medium; }),
            "https" => await _cf.ApplyAsync(Cf, (g, a, z) => g.SetAlwaysHttpsAsync(a, z, value), c => c.AlwaysUseHttps = value),
            "bot" => await _cf.ApplyAsync(Cf, (g, a, z) => g.SetBotFightModeAsync(a, z, value), c => c.BotFightMode = value),
            _ => new CfResult(false, "Unknown setting.", false)
        };

        Flash(result);
        return RedirectToTab("overview");
    }

    // ---------------- DNS ----------------

    public async Task<IActionResult> OnPostAddRecordAsync(string type, string name, string content, int ttl, bool proxied, int? priority)
    {
        if (!await LoadAsync()) return NotFound();

        var record = await _cf.Gateway.CreateRecordAsync(Cf.Account!, Cf.ZoneId, type,
            string.IsNullOrWhiteSpace(name) ? Cf.Domain!.DomainName : name, content, ttl, proxied, priority);

        TempData[record != null ? "Success" : "Error"] = record != null ? "DNS record created." : "Could not create the record.";
        return RedirectToTab("dns");
    }

    public async Task<IActionResult> OnPostDeleteRecordAsync(string recordId)
    {
        if (!await LoadAsync()) return NotFound();
        var result = await _cf.Gateway.DeleteRecordAsync(Cf.Account!, Cf.ZoneId, recordId);
        Flash(result);
        return RedirectToTab("dns");
    }

    public async Task<IActionResult> OnPostToggleProxyAsync(string recordId, string type, string name, string content, int ttl, bool proxied)
    {
        if (!await LoadAsync()) return NotFound();
        var result = await _cf.ToggleProxyAsync(Cf, recordId, type, name, content, ttl <= 1 ? 1 : ttl, proxied);
        Flash(result);
        return RedirectToTab("dns");
    }

    // ---------------- SSL / TLS ----------------

    public async Task<IActionResult> OnPostSslAsync(CfSslMode sslMode, string minTls, bool tls13, bool opportunistic)
    {
        if (!await LoadAsync()) return NotFound();

        await _cf.ApplyAsync(Cf, (g, a, z) => g.SetSslModeAsync(a, z, sslMode), c => c.SslMode = sslMode);
        await _cf.ApplyAsync(Cf, (g, a, z) => g.SetMinTlsVersionAsync(a, z, minTls), c => c.MinTlsVersion = minTls);
        await _cf.ApplyAsync(Cf, (g, a, z) => g.SetTls13Async(a, z, tls13), c => c.Tls13 = tls13);
        Cf.OpportunisticEncryption = opportunistic;
        await _db.SaveChangesAsync();

        TempData["Success"] = "SSL/TLS settings saved.";
        return RedirectToTab("ssl");
    }

    public async Task<IActionResult> OnPostOrderCertAsync()
    {
        if (!await LoadAsync()) return NotFound();
        var domainName = Cf.Domain!.DomainName;
        var result = await _cf.Gateway.OrderAdvancedCertAsync(Cf.Account!, Cf.ZoneId, new[] { domainName, $"*.{domainName}" });
        Flash(result);
        return RedirectToTab("ssl");
    }

    // ---------------- Speed ----------------

    public async Task<IActionResult> OnPostSpeedAsync(bool minifyCss, bool minifyJs, bool minifyHtml,
        bool brotli, bool http2, bool http3, bool rocketLoader, bool earlyHints, CfPolish polish, bool webp)
    {
        if (!await LoadAsync()) return NotFound();

        await _cf.ApplyAsync(Cf, (g, a, z) => g.SetMinifyAsync(a, z, minifyCss, minifyJs, minifyHtml),
            c => { c.MinifyCss = minifyCss; c.MinifyJs = minifyJs; c.MinifyHtml = minifyHtml; });
        await _cf.ApplyAsync(Cf, (g, a, z) => g.SetBrotliAsync(a, z, brotli), c => c.Brotli = brotli);
        await _cf.ApplyAsync(Cf, (g, a, z) => g.SetHttp2Async(a, z, http2), c => c.Http2 = http2);
        await _cf.ApplyAsync(Cf, (g, a, z) => g.SetHttp3Async(a, z, http3), c => c.Http3 = http3);
        await _cf.ApplyAsync(Cf, (g, a, z) => g.SetRocketLoaderAsync(a, z, rocketLoader), c => c.RocketLoader = rocketLoader);
        await _cf.ApplyAsync(Cf, (g, a, z) => g.SetEarlyHintsAsync(a, z, earlyHints), c => c.EarlyHints = earlyHints);
        await _cf.ApplyAsync(Cf, (g, a, z) => g.SetPolishAsync(a, z, polish, webp), c => { c.Polish = polish; c.WebpConversion = webp; });

        TempData["Success"] = "Speed settings saved.";
        return RedirectToTab("speed");
    }

    // ---------------- Caching ----------------

    public async Task<IActionResult> OnPostCachingAsync(CfCacheLevel cacheLevel, int browserTtl)
    {
        if (!await LoadAsync()) return NotFound();
        await _cf.ApplyAsync(Cf, (g, a, z) => g.SetCacheLevelAsync(a, z, cacheLevel), c => c.CacheLevel = cacheLevel);
        await _cf.ApplyAsync(Cf, (g, a, z) => g.SetBrowserCacheTtlAsync(a, z, browserTtl), c => c.BrowserCacheTtl = browserTtl);
        TempData["Success"] = "Caching settings saved.";
        return RedirectToTab("caching");
    }

    public async Task<IActionResult> OnPostPurgeAllAsync()
    {
        if (!await LoadAsync()) return NotFound();
        var result = await _cf.PurgeAllAsync(_userId!, Cf);
        Flash(result);
        return RedirectToTab("caching");
    }

    public async Task<IActionResult> OnPostPurgeUrlsAsync(string urls)
    {
        if (!await LoadAsync()) return NotFound();
        var list = (urls ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = await _cf.PurgeUrlsAsync(_userId!, Cf, list);
        Flash(result);
        return RedirectToTab("caching");
    }

    // ---------------- Firewall ----------------

    public async Task<IActionResult> OnPostSecurityLevelAsync(CfSecurityLevel level)
    {
        if (!await LoadAsync()) return NotFound();
        var result = await _cf.ApplyAsync(Cf, (g, a, z) => g.SetSecurityLevelAsync(a, z, level),
            c => { c.SecurityLevel = level; c.UnderAttackMode = level == CfSecurityLevel.UnderAttack; });
        Flash(result);
        return RedirectToTab("firewall");
    }

    public async Task<IActionResult> OnPostAddFirewallRuleAsync(string description, string expression, string action)
    {
        if (!await LoadAsync()) return NotFound();

        var rule = await _cf.Gateway.CreateFirewallRuleAsync(Cf.Account!, Cf.ZoneId, description, expression, action);
        if (rule != null)
        {
            _db.CloudflareRules.Add(new CloudflareRule
            {
                CloudflareDomainId = Cf.Id, Type = CloudflareRuleType.FirewallRule, RuleId = rule.RuleId,
                Name = description, Expression = expression, Action = action, IsActive = true, CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }

        TempData[rule != null ? "Success" : "Error"] = rule != null ? "Firewall rule created." : "Could not create the rule.";
        return RedirectToTab("firewall");
    }

    public async Task<IActionResult> OnPostDeleteFirewallRuleAsync(string ruleId)
    {
        if (!await LoadAsync()) return NotFound();
        var result = await _cf.Gateway.DeleteFirewallRuleAsync(Cf.Account!, Cf.ZoneId, ruleId);
        var local = await _db.CloudflareRules.FirstOrDefaultAsync(r => r.CloudflareDomainId == Cf.Id && r.RuleId == ruleId);
        if (local != null) { _db.CloudflareRules.Remove(local); await _db.SaveChangesAsync(); }
        Flash(result);
        return RedirectToTab("firewall");
    }

    // ---------------- Page rules ----------------

    public async Task<IActionResult> OnPostAddPageRuleAsync(string name, string urlPattern, string actionsJson, string actionsLabel)
    {
        if (!await LoadAsync()) return NotFound();

        var rule = await _cf.Gateway.CreatePageRuleAsync(Cf.Account!, Cf.ZoneId, urlPattern, actionsJson);
        if (rule != null)
        {
            var maxPriority = await _db.CloudflareRules
                .Where(r => r.CloudflareDomainId == Cf.Id && r.Type == CloudflareRuleType.PageRule)
                .Select(r => (int?)r.Priority).MaxAsync() ?? 0;

            _db.CloudflareRules.Add(new CloudflareRule
            {
                CloudflareDomainId = Cf.Id, Type = CloudflareRuleType.PageRule, RuleId = rule.RuleId,
                Name = name, Expression = urlPattern, Action = actionsLabel, Priority = maxPriority + 1,
                IsActive = true, CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }

        TempData[rule != null ? "Success" : "Error"] = rule != null ? "Page rule created." : "Could not create the page rule.";
        return RedirectToTab("pagerules");
    }

    public async Task<IActionResult> OnPostDeletePageRuleAsync(int id)
    {
        if (!await LoadAsync()) return NotFound();
        var rule = await _db.CloudflareRules.FirstOrDefaultAsync(r => r.Id == id && r.CloudflareDomainId == Cf.Id);
        if (rule != null)
        {
            await _cf.Gateway.DeletePageRuleAsync(Cf.Account!, Cf.ZoneId, rule.RuleId);
            _db.CloudflareRules.Remove(rule);
            await _db.SaveChangesAsync();
        }
        TempData["Success"] = "Page rule deleted.";
        return RedirectToTab("pagerules");
    }

    // ---------------- WordPress ----------------

    public async Task<IActionResult> OnPostWordPressPresetAsync()
    {
        if (!await LoadAsync()) return NotFound();
        await _cf.ApplyWordPressPresetAsync(_userId!, Cf);
        await _auditLog.LogAsync("WordPressPreset", "CloudflareDomain", Cf.Id.ToString(), Cf.Domain?.DomainName ?? "");
        TempData["Success"] = "WordPress optimisation applied — cache rules, SSL and minification are set.";
        return RedirectToTab("pagerules");
    }

    // ---------------- Analytics export ----------------

    public async Task<IActionResult> OnGetExportAsync()
    {
        if (!await LoadAsync()) return NotFound();
        await LoadAnalyticsAsync();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Date,Requests,CachedRequests,Bandwidth,CachedBandwidth,Threats,PageViews,UniqueVisitors,CacheHitRate");
        foreach (var a in Analytics)
            sb.AppendLine($"{a.Date:yyyy-MM-dd},{a.Requests},{a.CachedRequests},{a.Bandwidth},{a.CachedBandwidth},{a.Threats},{a.PageViews},{a.UniqueVisitors},{a.CacheHitRate}");

        return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "text/csv",
            $"cloudflare-{Cf.Domain?.DomainName}-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private void Flash(CfResult result) => TempData[result.Success ? "Success" : "Error"] = result.Message;

    private IActionResult RedirectToTab(string tab) => RedirectToPage(new { domainId = DomainId, tab });
}
