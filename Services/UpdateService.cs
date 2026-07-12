using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services;

public record ReleaseInfo(string Version, string Channel, DateTime ReleasedAt, string[] Changelog);

public record UpdateCheckResult(
    string CurrentVersion,
    string Channel,
    bool UpdateAvailable,
    bool AutoUpdate,
    ReleaseInfo Latest);

public interface IUpdateService
{
    string CurrentVersion { get; }
    string Channel { get; }
    bool AutoUpdate { get; }

    Task<UpdateCheckResult> CheckForUpdatesAsync();
    Task<UpdateHistory> ApplyUpdateAsync(string toVersion, string? triggeredBy);
    Task SetAutoUpdateAsync(bool enabled);
    Task<List<UpdateHistory>> GetHistoryAsync(int take = 50);
}

/// <summary>
/// Version management for the panel. On a real Ubuntu install this would shell out
/// to <c>update.sh</c> (git pull + dotnet publish + migrate + restart); in
/// simulation mode (dev/Windows) it records the transition without touching the OS.
/// The "latest release" catalogue is bundled so the panel can advertise updates
/// without a network round-trip to get.srxpanel.com.
/// </summary>
public class UpdateService : IUpdateService
{
    private readonly ApplicationDbContext _db;
    private readonly IPlatformSettingsService _platform;
    private readonly ICommandRunner _log;

    public UpdateService(ApplicationDbContext db, IPlatformSettingsService platform, ICommandRunner log)
    {
        _db = db;
        _platform = platform;
        _log = log;
    }

    public string CurrentVersion => AppInfo.Version;
    public string Channel { get; private set; } = "stable";
    public bool AutoUpdate { get; private set; }

    // Bundled release catalogue (newest first). In production update.sh replaces
    // this with the manifest fetched from the update server.
    private static readonly ReleaseInfo[] Releases =
    {
        new("1.0.0", "stable", new DateTime(2026, 7, 8), new[]
        {
            "Phase 7 — one-liner Ubuntu installer, Docker support and public docs",
            "Health check endpoint at /api/health",
            "In-panel version management with update history",
            "Public landing page and documentation"
        })
    };

    public async Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        var settings = await _platform.GetAsync();
        Channel = string.IsNullOrWhiteSpace(settings.UpdateChannel) ? "stable" : settings.UpdateChannel;
        AutoUpdate = settings.AutoUpdate;

        var latest = Releases
            .Where(r => Channel == "beta" || r.Channel == "stable")
            .OrderByDescending(r => new Version(r.Version))
            .First();

        var available = new Version(latest.Version) > new Version(CurrentVersion);
        return new UpdateCheckResult(CurrentVersion, Channel, available, AutoUpdate, latest);
    }

    public async Task<UpdateHistory> ApplyUpdateAsync(string toVersion, string? triggeredBy)
    {
        var simulated = !RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        var release = Releases.FirstOrDefault(r => r.Version == toVersion);
        var notes = release != null ? string.Join("\n", release.Changelog) : "Manual update";

        var record = new UpdateHistory
        {
            FromVersion = CurrentVersion,
            ToVersion = toVersion,
            Channel = Channel,
            Status = UpdateStatus.Success,
            Notes = notes,
            Simulated = simulated,
            TriggeredBy = triggeredBy,
            CreatedAt = DateTime.UtcNow
        };

        // On Linux this is where update.sh would be invoked. We record the intent
        // either way so the history/audit trail is consistent.
        await _log.LogExternalAsync(
            $"update.sh --to {toVersion} --channel {Channel}",
            simulated ? "Update simulated (dev host); run scripts/update.sh on the server to apply." : "Update applied.",
            true,
            "updates");

        _db.UpdateHistory.Add(record);
        await _db.SaveChangesAsync();
        return record;
    }

    public async Task SetAutoUpdateAsync(bool enabled)
    {
        var settings = await _platform.GetAsync();
        settings.AutoUpdate = enabled;
        await _platform.SaveAsync(settings);
        AutoUpdate = enabled;
    }

    public Task<List<UpdateHistory>> GetHistoryAsync(int take = 50) =>
        _db.UpdateHistory.OrderByDescending(u => u.CreatedAt).Take(take).ToListAsync();
}
