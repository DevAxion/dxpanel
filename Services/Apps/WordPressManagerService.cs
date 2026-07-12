using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Apps;

public record RepoAsset(string Slug, string Name, string Version, string Description, int Downloads);
public record WpHealth(int Score, string WpVersion, bool CoreUpToDate, string PhpVersion, bool PhpOk,
    string ActiveTheme, int PluginsActive, int PluginsInactive, int PluginsNeedUpdate, List<string> Recommendations);

/// <summary>Safe subset of WP-CLI commands exposed in the panel.</summary>
public enum WpCliCommand
{
    FlushCache,
    RegenerateThumbnails,
    SearchReplaceUrl,
    ExportDatabase,
    ImportDatabase
}

public interface IWordPressManager
{
    Task<List<WpAsset>> GetAssetsAsync(int installationId, WpAssetType type);
    Task<WpHealth> GetHealthAsync(int installationId);
    Task SetActiveAsync(int installationId, int assetId, bool active);
    Task DeleteAssetAsync(int installationId, int assetId);
    Task UpdateAssetAsync(int installationId, int assetId);
    Task<int> UpdateAllAsync(int installationId);
    Task InstallFromRepoAsync(int installationId, WpAssetType type, string slug, bool activate);
    Task ActivateThemeAsync(int installationId, int assetId);
    List<RepoAsset> SearchRepo(WpAssetType type, string? query);
    Task<string> RunCliAsync(int installationId, WpCliCommand command, string? argument);
    Task SetFlagAsync(int installationId, string flag, bool value);
}

public class WordPressManagerService : IWordPressManager
{
    private const string ServiceName = "wp-cli";
    private readonly ApplicationDbContext _db;
    private readonly ICommandRunner _runner;

    public WordPressManagerService(ApplicationDbContext db, ICommandRunner runner)
    {
        _db = db;
        _runner = runner;
    }

    // Curated slice of the WordPress.org repository (a real deployment queries the API).
    private static readonly RepoAsset[] RepoPlugins =
    {
        new("wordfence", "Wordfence Security", "7.11.4", "Firewall and malware scanner for WordPress.", 4_000_000),
        new("yoast-seo", "Yoast SEO", "22.3", "The #1 WordPress SEO plugin.", 5_000_000),
        new("contact-form-7", "Contact Form 7", "5.9.2", "Simple but flexible contact forms.", 5_000_000),
        new("wp-super-cache", "WP Super Cache", "1.12.4", "Static caching plugin for speed.", 2_000_000),
        new("elementor", "Elementor Website Builder", "3.20.1", "Drag-and-drop page builder.", 5_000_000),
        new("updraftplus", "UpdraftPlus Backups", "1.24.2", "Backup and restore made simple.", 3_000_000),
        new("woocommerce", "WooCommerce", "8.6.1", "The open-source e-commerce platform.", 5_000_000)
    };

    private static readonly RepoAsset[] RepoThemes =
    {
        new("astra", "Astra", "4.6.9", "Fast, lightweight, customizable theme.", 1_000_000),
        new("kadence", "Kadence", "1.2.4", "Flexible block-based theme.", 400_000),
        new("generatepress", "GeneratePress", "3.4.0", "Lightweight, modular theme.", 400_000),
        new("twentytwentyfour", "Twenty Twenty-Four", "1.2", "The default WordPress theme.", 3_000_000)
    };

    public Task<List<WpAsset>> GetAssetsAsync(int installationId, WpAssetType type) =>
        _db.WpAssets.Where(a => a.InstallationId == installationId && a.Type == type)
            .OrderByDescending(a => a.IsActive).ThenBy(a => a.Name).ToListAsync();

    public async Task<WpHealth> GetHealthAsync(int installationId)
    {
        var inst = await _db.AppInstallations.Include(i => i.AppDefinition)
            .FirstOrDefaultAsync(i => i.Id == installationId);
        var assets = await _db.WpAssets.Where(a => a.InstallationId == installationId).ToListAsync();

        var plugins = assets.Where(a => a.Type == WpAssetType.Plugin).ToList();
        var theme = assets.FirstOrDefault(a => a.Type == WpAssetType.Theme && a.IsActive);
        var coreUpToDate = inst?.Status != AppInstallStatus.UpdateAvailable;
        var phpOk = string.Compare(inst?.PhpVersion ?? "8.3", "8.1", StringComparison.Ordinal) >= 0;
        var needUpdate = assets.Count(a => a.UpdateAvailable);

        var score = 100;
        var recs = new List<string>();
        if (!coreUpToDate) { score -= 25; recs.Add($"Update WordPress core to {inst?.AvailableVersion}."); }
        if (!phpOk) { score -= 20; recs.Add("Upgrade PHP to 8.1 or newer for better performance and security."); }
        if (needUpdate > 0) { score -= Math.Min(20, needUpdate * 10); recs.Add($"{needUpdate} plugin/theme update(s) available."); }
        if (plugins.Count(p => !p.IsActive) > 2) { score -= 10; recs.Add("Delete inactive plugins you no longer need."); }
        if (theme == null) { score -= 15; recs.Add("No active theme detected."); }
        if (recs.Count == 0) recs.Add("Everything looks healthy. Nice work!");

        return new WpHealth(Math.Max(0, score), inst?.InstalledVersion ?? "-", coreUpToDate,
            inst?.PhpVersion ?? "-", phpOk, theme?.Name ?? "None",
            plugins.Count(p => p.IsActive), plugins.Count(p => !p.IsActive), needUpdate, recs);
    }

    public async Task SetActiveAsync(int installationId, int assetId, bool active)
    {
        var a = await FindAsync(installationId, assetId);
        if (a == null) return;
        a.IsActive = active;
        await _db.SaveChangesAsync();
        await _runner.RunAsync($"wp {a.Type.ToString().ToLower()} {(active ? "activate" : "deactivate")} {a.Slug}", ServiceName);
    }

    public async Task ActivateThemeAsync(int installationId, int assetId)
    {
        var themes = await _db.WpAssets.Where(a => a.InstallationId == installationId && a.Type == WpAssetType.Theme).ToListAsync();
        var target = themes.FirstOrDefault(t => t.Id == assetId);
        if (target == null) return;
        foreach (var t in themes) t.IsActive = t.Id == assetId;
        await _db.SaveChangesAsync();
        await _runner.RunAsync($"wp theme activate {target.Slug}", ServiceName);
    }

    public async Task DeleteAssetAsync(int installationId, int assetId)
    {
        var a = await FindAsync(installationId, assetId);
        if (a == null) return;
        await _runner.RunAsync($"wp {a.Type.ToString().ToLower()} delete {a.Slug}", ServiceName);
        _db.WpAssets.Remove(a);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAssetAsync(int installationId, int assetId)
    {
        var a = await FindAsync(installationId, assetId);
        if (a == null || !a.UpdateAvailable) return;
        await _runner.RunAsync($"wp {a.Type.ToString().ToLower()} update {a.Slug}", ServiceName);
        a.Version = a.LatestVersion ?? a.Version;
        a.UpdateAvailable = false;
        a.LatestVersion = null;
        await _db.SaveChangesAsync();
    }

    public async Task<int> UpdateAllAsync(int installationId)
    {
        var pending = await _db.WpAssets.Where(a => a.InstallationId == installationId && a.UpdateAvailable).ToListAsync();
        foreach (var a in pending)
        {
            a.Version = a.LatestVersion ?? a.Version;
            a.UpdateAvailable = false;
            a.LatestVersion = null;
        }
        if (pending.Count > 0)
        {
            await _runner.RunAsync("wp plugin update --all && wp theme update --all", ServiceName);
            await _db.SaveChangesAsync();
        }
        return pending.Count;
    }

    public async Task InstallFromRepoAsync(int installationId, WpAssetType type, string slug, bool activate)
    {
        if (await _db.WpAssets.AnyAsync(a => a.InstallationId == installationId && a.Type == type && a.Slug == slug)) return;
        var repo = (type == WpAssetType.Plugin ? RepoPlugins : RepoThemes).FirstOrDefault(r => r.Slug == slug);
        if (repo == null) return;

        await _runner.RunAsync($"wp {type.ToString().ToLower()} install {slug}{(activate ? " --activate" : "")}", ServiceName);

        if (type == WpAssetType.Theme && activate)
        {
            foreach (var t in await _db.WpAssets.Where(a => a.InstallationId == installationId && a.Type == WpAssetType.Theme).ToListAsync())
                t.IsActive = false;
        }

        _db.WpAssets.Add(new WpAsset
        {
            InstallationId = installationId, Type = type, Slug = repo.Slug, Name = repo.Name,
            Version = repo.Version, IsActive = activate, InstalledAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    public List<RepoAsset> SearchRepo(WpAssetType type, string? query)
    {
        var source = type == WpAssetType.Plugin ? RepoPlugins : RepoThemes;
        if (string.IsNullOrWhiteSpace(query)) return source.ToList();
        var q = query.Trim();
        return source.Where(r => r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                              || r.Slug.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public async Task<string> RunCliAsync(int installationId, WpCliCommand command, string? argument)
    {
        var inst = await _db.AppInstallations.FirstOrDefaultAsync(i => i.Id == installationId);
        if (inst == null) return "Installation not found.";

        var (cmd, message) = command switch
        {
            WpCliCommand.FlushCache => ("wp cache flush", "Object cache flushed."),
            WpCliCommand.RegenerateThumbnails => ("wp media regenerate --yes", "Thumbnails regenerated for all attachments."),
            WpCliCommand.SearchReplaceUrl => ($"wp search-replace '{inst.SiteUrl}' '{argument}'", $"URLs rewritten to {argument}."),
            WpCliCommand.ExportDatabase => ("wp db export", "Database exported to the site root."),
            WpCliCommand.ImportDatabase => ("wp db import", "Database imported."),
            _ => ("wp --info", "Command executed.")
        };

        var result = await _runner.RunAsync($"{cmd} --path={inst.InstallPath}", ServiceName);
        return result.Simulated ? $"{message} (simulated)" : result.Output;
    }

    public async Task SetFlagAsync(int installationId, string flag, bool value)
    {
        var inst = await _db.AppInstallations.FirstOrDefaultAsync(i => i.Id == installationId);
        if (inst == null) return;

        var cmd = flag switch
        {
            "debug" => $"wp config set WP_DEBUG {value.ToString().ToLower()} --raw",
            "https" => $"wp config set FORCE_SSL_ADMIN {value.ToString().ToLower()} --raw",
            "multisite" => $"wp config set WP_ALLOW_MULTISITE {value.ToString().ToLower()} --raw",
            _ => "wp --info"
        };
        await _runner.RunAsync($"{cmd} --path={inst.InstallPath}", ServiceName);
    }

    private Task<WpAsset?> FindAsync(int installationId, int assetId) =>
        _db.WpAssets.FirstOrDefaultAsync(a => a.Id == assetId && a.InstallationId == installationId);
}
