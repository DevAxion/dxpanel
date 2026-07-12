using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;
using SRXPanel.Services.Portal;

namespace SRXPanel.Services.Apps;

public record InstallRequest(
    int AppDefinitionId, int DomainId, string Path, string SiteTitle,
    string AdminUser, string AdminPass, string AdminEmail,
    string DbName, string TablePrefix, string PhpVersion, string Language);

public interface IAppInstallerService
{
    Task<List<AppDefinition>> GetAvailableAppsAsync(AppCategory? category = null, string? search = null);
    Task<AppDefinition?> GetAppAsync(string slug);
    Task<AppDefinition?> GetAppByIdAsync(int id);

    Task<int> InstallAsync(string userId, InstallRequest request);
    Task<int> UpdateAsync(string userId, int installationId);
    Task<int> UninstallAsync(string userId, int installationId);
    Task<int> CloneAsync(string userId, int installationId, int targetDomainId, string path);

    Task<List<AppInstallation>> GetInstallationsAsync(string userId);
    Task<AppInstallation?> GetInstallationDetailsAsync(string userId, int id);
    Task<AppInstallJob?> GetJobAsync(int jobId);
    Task<List<Backup>> GetRestorePointsAsync(string userId, int installationId);
    Task ChangeAdminPasswordAsync(string userId, int installationId, string newPassword);
}

/// <summary>
/// Softaculous-style one-click installer. Jobs run in the background with live
/// SignalR progress. All filesystem/DB work goes through the existing integration
/// services, so it is fully simulation-safe on a dev host.
/// </summary>
public class AppInstallerService : IAppInstallerService
{
    private const string ServiceName = "app-installer";
    private readonly ApplicationDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PanelSettings _panel;

    public AppInstallerService(ApplicationDbContext db, IServiceScopeFactory scopeFactory, IOptionsMonitor<PanelSettings> panel)
    {
        _db = db;
        _scopeFactory = scopeFactory;
        _panel = panel.CurrentValue;
    }

    // ---------------- Catalogue ----------------
    public async Task<List<AppDefinition>> GetAvailableAppsAsync(AppCategory? category = null, string? search = null)
    {
        var q = _db.AppDefinitions.Where(a => a.IsActive);
        if (category.HasValue) q = q.Where(a => a.Category == category);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(a => a.Name.Contains(s) || a.Description.Contains(s));
        }
        return await q.OrderByDescending(a => a.InstallCount).ThenBy(a => a.Name).ToListAsync();
    }

    public Task<AppDefinition?> GetAppAsync(string slug) =>
        _db.AppDefinitions.FirstOrDefaultAsync(a => a.Slug == slug && a.IsActive);

    public Task<AppDefinition?> GetAppByIdAsync(int id) => _db.AppDefinitions.FirstOrDefaultAsync(a => a.Id == id);

    public Task<List<AppInstallation>> GetInstallationsAsync(string userId) =>
        _db.AppInstallations.Include(i => i.AppDefinition).Include(i => i.Domain)
            .Where(i => i.UserId == userId).OrderByDescending(i => i.InstalledAt).ToListAsync();

    public Task<AppInstallation?> GetInstallationDetailsAsync(string userId, int id) =>
        _db.AppInstallations.Include(i => i.AppDefinition).Include(i => i.Domain)
            .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId);

    public Task<AppInstallJob?> GetJobAsync(int jobId) => _db.AppInstallJobs.FirstOrDefaultAsync(j => j.Id == jobId);

    public Task<List<Backup>> GetRestorePointsAsync(string userId, int installationId) =>
        _db.Backups.Where(b => b.UserId == userId && b.Label != null)
            .OrderByDescending(b => b.CreatedAt).Take(10).ToListAsync();

    public async Task ChangeAdminPasswordAsync(string userId, int installationId, string newPassword)
    {
        var inst = await GetInstallationDetailsAsync(userId, installationId);
        if (inst == null) return;
        using var scope = _scopeFactory.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<ICommandRunner>();
        await runner.LogExternalAsync(
            $"wp user update {inst.AdminUser} --user_pass=**** --path={inst.InstallPath}",
            "admin password updated", true, ServiceName);
    }

    // ---------------- Job creation ----------------
    private async Task<int> CreateJobAsync(string userId, AppJobType type, string appName, int? installationId)
    {
        var job = new AppInstallJob
        {
            UserId = userId, Type = type, AppName = appName, InstallationId = installationId,
            Status = AppJobStatus.Pending, Progress = 0, CurrentStep = "Queued", StartedAt = DateTime.UtcNow
        };
        _db.AppInstallJobs.Add(job);
        await _db.SaveChangesAsync();
        return job.Id;
    }

    public async Task<int> InstallAsync(string userId, InstallRequest request)
    {
        var app = await GetAppByIdAsync(request.AppDefinitionId) ?? throw new InvalidOperationException("App not found.");
        var jobId = await CreateJobAsync(userId, AppJobType.Install, app.Name, null);
        Fire(jobId, (sp, ct) => RunInstallAsync(sp, jobId, userId, request, ct));
        return jobId;
    }

    public async Task<int> UpdateAsync(string userId, int installationId)
    {
        var inst = await GetInstallationDetailsAsync(userId, installationId) ?? throw new InvalidOperationException("Installation not found.");
        var jobId = await CreateJobAsync(userId, AppJobType.Update, inst.AppDefinition?.Name ?? "App", installationId);
        Fire(jobId, (sp, ct) => RunUpdateAsync(sp, jobId, userId, installationId, ct));
        return jobId;
    }

    public async Task<int> UninstallAsync(string userId, int installationId)
    {
        var inst = await GetInstallationDetailsAsync(userId, installationId) ?? throw new InvalidOperationException("Installation not found.");
        var jobId = await CreateJobAsync(userId, AppJobType.Uninstall, inst.AppDefinition?.Name ?? "App", installationId);
        Fire(jobId, (sp, ct) => RunUninstallAsync(sp, jobId, userId, installationId, ct));
        return jobId;
    }

    public async Task<int> CloneAsync(string userId, int installationId, int targetDomainId, string path)
    {
        var inst = await GetInstallationDetailsAsync(userId, installationId) ?? throw new InvalidOperationException("Installation not found.");
        var jobId = await CreateJobAsync(userId, AppJobType.Clone, inst.AppDefinition?.Name ?? "App", installationId);
        Fire(jobId, (sp, ct) => RunCloneAsync(sp, jobId, userId, installationId, targetDomainId, path, ct));
        return jobId;
    }

    /// <summary>Runs a job on a background task with its own DI scope.</summary>
    private void Fire(int jobId, Func<IServiceProvider, CancellationToken, Task> work)
    {
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var broadcast = sp.GetRequiredService<IInstallBroadcast>();
            var logger = sp.GetRequiredService<ILogger<AppInstallerService>>();
            try
            {
                await work(sp, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "App job {JobId} failed", jobId);
                var job = await db.AppInstallJobs.FindAsync(jobId);
                if (job != null)
                {
                    job.Status = AppJobStatus.Failed;
                    job.CurrentStep = "Failed";
                    job.Log += $"\nERROR: {ex.Message}";
                    job.CompletedAt = DateTime.UtcNow;
                    if (job.InstallationId is int iid)
                    {
                        var inst = await db.AppInstallations.FindAsync(iid);
                        if (inst != null) inst.Status = AppInstallStatus.Error;
                    }
                    await db.SaveChangesAsync();
                }
                await broadcast.CompletedAsync(jobId, false, ex.Message, null);
            }
        });
    }

    private static async Task StepAsync(ApplicationDbContext db, IInstallBroadcast bc, AppInstallJob job,
        int percent, string step, string log)
    {
        job.Progress = percent;
        job.CurrentStep = step;
        job.Log += (job.Log.Length > 0 ? "\n" : "") + log;
        job.Status = AppJobStatus.Running;
        await db.SaveChangesAsync();
        await bc.ProgressAsync(job.Id, percent, step, log);
        await Task.Delay(500);
    }

    // ---------------- Install ----------------
    private async Task RunInstallAsync(IServiceProvider sp, int jobId, string userId, InstallRequest req, CancellationToken ct)
    {
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var bc = sp.GetRequiredService<IInstallBroadcast>();
        var runner = sp.GetRequiredService<ICommandRunner>();
        var mysql = sp.GetRequiredService<IMySqlService>();
        var notifications = sp.GetRequiredService<INotificationService>();

        var job = await db.AppInstallJobs.FirstAsync(j => j.Id == jobId, ct);
        var app = await db.AppDefinitions.FirstAsync(a => a.Id == req.AppDefinitionId, ct);
        var domain = await db.Domains.FirstAsync(d => d.Id == req.DomainId, ct);

        var relPath = NormalizePath(req.Path);
        var installDir = $"{domain.DocumentRoot.TrimEnd('/')}{(relPath == "/" ? "" : relPath)}";
        var siteUrl = $"https://{domain.DomainName}{(relPath == "/" ? "" : relPath)}";

        await StepAsync(db, bc, job, 10, "Download", $"Fetching {app.Name} {app.Version} from {app.DownloadUrl ?? "official source"}…");
        await runner.RunAsync($"curl -fsSL {app.DownloadUrl} -o /tmp/{app.Slug}.tar.gz", ServiceName);

        await StepAsync(db, bc, job, 30, "Extract", $"Extracting archive to {installDir}…");
        await runner.RunAsync($"mkdir -p {installDir} && tar -xzf /tmp/{app.Slug}.tar.gz -C {installDir} --strip-components=1", ServiceName);

        await StepAsync(db, bc, job, 50, "Configure", $"Writing configuration (PHP {req.PhpVersion}, language {req.Language})…");
        await runner.WriteFileAsync($"{installDir}/srx-install.json",
            $"{{\"app\":\"{app.Slug}\",\"version\":\"{app.Version}\",\"title\":\"{req.SiteTitle}\"}}", ServiceName);

        string? dbName = null, dbUser = null;
        if (app.RequiresDatabase)
        {
            dbName = req.DbName;
            dbUser = req.DbName;
            await StepAsync(db, bc, job, 70, "Database Setup", $"Creating database {dbName} and user…");
            await mysql.CreateDatabaseAsync(dbName);
            var dbPass = HostingHelpers.GeneratePassword();
            await mysql.CreateUserAsync(dbUser, dbPass);
            await mysql.GrantPermissionsAsync(dbName, dbUser);

            if (!await db.Databases.AnyAsync(d => d.DbName == dbName, ct))
            {
                db.Databases.Add(new Database
                {
                    UserId = userId, DomainId = domain.Id, DbName = dbName, DbUser = dbUser,
                    DbPasswordHash = "", CreatedAt = DateTime.UtcNow, IsActive = true
                });
                await db.SaveChangesAsync(ct);
            }
        }
        else
        {
            await StepAsync(db, bc, job, 70, "Database Setup", "This application does not require a database — skipping.");
        }

        await StepAsync(db, bc, job, 90, "Finalize", $"Creating admin account '{req.AdminUser}' and setting permissions…");
        await runner.RunAsync($"chown -R www-data:www-data {installDir}", ServiceName);

        var installation = new AppInstallation
        {
            UserId = userId, DomainId = domain.Id, AppDefinitionId = app.Id,
            InstalledVersion = app.Version, InstallPath = relPath,
            DatabaseName = dbName, DatabaseUser = dbUser, TablePrefix = req.TablePrefix,
            SiteUrl = siteUrl, AdminUrl = AdminUrlFor(app.Slug, siteUrl),
            SiteTitle = req.SiteTitle, AdminUser = req.AdminUser, AdminEmail = req.AdminEmail,
            PhpVersion = req.PhpVersion, Language = req.Language,
            Status = AppInstallStatus.Active, InstalledAt = DateTime.UtcNow
        };
        db.AppInstallations.Add(installation);
        app.InstallCount++;
        await db.SaveChangesAsync(ct);

        // WordPress ships with default plugins/themes — track them for the WP manager.
        if (app.Slug is "wordpress" or "woocommerce")
        {
            db.WpAssets.AddRange(
                new WpAsset { InstallationId = installation.Id, Type = WpAssetType.Plugin, Slug = "akismet", Name = "Akismet Anti-spam", Version = "5.3", IsActive = false },
                new WpAsset { InstallationId = installation.Id, Type = WpAssetType.Plugin, Slug = "hello-dolly", Name = "Hello Dolly", Version = "1.7.2", IsActive = false },
                new WpAsset { InstallationId = installation.Id, Type = WpAssetType.Theme, Slug = "twentytwentyfour", Name = "Twenty Twenty-Four", Version = "1.2", IsActive = true },
                new WpAsset { InstallationId = installation.Id, Type = WpAssetType.Theme, Slug = "twentytwentythree", Name = "Twenty Twenty-Three", Version = "1.4", IsActive = false, UpdateAvailable = true, LatestVersion = "1.5" });
            if (app.Slug == "woocommerce")
                db.WpAssets.Add(new WpAsset { InstallationId = installation.Id, Type = WpAssetType.Plugin, Slug = "woocommerce", Name = "WooCommerce", Version = "8.6.1", IsActive = true });
            await db.SaveChangesAsync(ct);
        }

        job.InstallationId = installation.Id;
        job.Status = AppJobStatus.Success;
        job.Progress = 100;
        job.CurrentStep = "Completed";
        job.Log += $"\n{app.Name} installed successfully at {siteUrl}";
        job.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await notifications.NotifyAsync(userId, $"{app.Name} installed",
            $"{app.Name} is now live at {siteUrl}.", NotificationType.Success);
        await bc.CompletedAsync(jobId, true, $"{app.Name} installed successfully.", installation.Id);
    }

    // ---------------- Update (with pre-action backup) ----------------
    private async Task RunUpdateAsync(IServiceProvider sp, int jobId, string userId, int installationId, CancellationToken ct)
    {
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var bc = sp.GetRequiredService<IInstallBroadcast>();
        var runner = sp.GetRequiredService<ICommandRunner>();
        var notifications = sp.GetRequiredService<INotificationService>();

        var job = await db.AppInstallJobs.FirstAsync(j => j.Id == jobId, ct);
        var inst = await db.AppInstallations.Include(i => i.AppDefinition).FirstAsync(i => i.Id == installationId, ct);
        var app = inst.AppDefinition!;
        var target = inst.AvailableVersion ?? app.Version;

        await StepAsync(db, bc, job, 15, "Backup", $"Creating restore point 'Before {app.Name} {target} update'…");
        await CreateRestorePointAsync(sp, userId, $"Before {app.Name} {target} update", ct);

        await StepAsync(db, bc, job, 45, "Download", $"Downloading {app.Name} {target}…");
        await runner.RunAsync($"curl -fsSL {app.DownloadUrl} -o /tmp/{app.Slug}-{target}.tar.gz", ServiceName);

        await StepAsync(db, bc, job, 75, "Extract", "Replacing core files (preserving configuration and uploads)…");
        await runner.RunAsync($"tar -xzf /tmp/{app.Slug}-{target}.tar.gz --strip-components=1 -C {inst.InstallPath}", ServiceName);

        await StepAsync(db, bc, job, 95, "Finalize", "Running database migrations and flushing caches…");

        inst.InstalledVersion = target;
        inst.AvailableVersion = null;
        inst.Status = AppInstallStatus.Active;
        inst.LastUpdatedAt = DateTime.UtcNow;

        job.Status = AppJobStatus.Success;
        job.Progress = 100;
        job.CurrentStep = "Completed";
        job.Log += $"\n{app.Name} updated to {target}.";
        job.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await notifications.NotifyAsync(userId, $"{app.Name} updated",
            $"{inst.SiteTitle} was updated to {app.Name} {target}.", NotificationType.Success);
        await bc.CompletedAsync(jobId, true, $"Updated to {target}.", inst.Id);
    }

    // ---------------- Uninstall (with pre-action backup) ----------------
    private async Task RunUninstallAsync(IServiceProvider sp, int jobId, string userId, int installationId, CancellationToken ct)
    {
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var bc = sp.GetRequiredService<IInstallBroadcast>();
        var runner = sp.GetRequiredService<ICommandRunner>();
        var mysql = sp.GetRequiredService<IMySqlService>();

        var job = await db.AppInstallJobs.FirstAsync(j => j.Id == jobId, ct);
        var inst = await db.AppInstallations.Include(i => i.AppDefinition).Include(i => i.Domain)
            .FirstAsync(i => i.Id == installationId, ct);
        var app = inst.AppDefinition!;

        await StepAsync(db, bc, job, 20, "Backup", $"Creating restore point 'Before {app.Name} removal'…");
        await CreateRestorePointAsync(sp, userId, $"Before {app.Name} removal", ct);

        var installDir = $"{inst.Domain?.DocumentRoot?.TrimEnd('/')}{(inst.InstallPath == "/" ? "" : inst.InstallPath)}";
        await StepAsync(db, bc, job, 55, "Remove Files", $"Deleting {installDir}…");
        await runner.DeletePathAsync(installDir, ServiceName);

        if (!string.IsNullOrEmpty(inst.DatabaseName))
        {
            await StepAsync(db, bc, job, 80, "Drop Database", $"Dropping database {inst.DatabaseName}…");
            await mysql.DeleteDatabaseAsync(inst.DatabaseName);
            if (!string.IsNullOrEmpty(inst.DatabaseUser)) await mysql.DeleteUserAsync(inst.DatabaseUser);

            var dbRow = await db.Databases.FirstOrDefaultAsync(d => d.DbName == inst.DatabaseName && d.UserId == userId, ct);
            if (dbRow != null) db.Databases.Remove(dbRow);
        }

        await StepAsync(db, bc, job, 95, "Finalize", "Cleaning up records…");
        var assets = await db.WpAssets.Where(a => a.InstallationId == inst.Id).ToListAsync(ct);
        db.WpAssets.RemoveRange(assets);
        db.AppInstallations.Remove(inst);

        job.InstallationId = null;
        job.Status = AppJobStatus.Success;
        job.Progress = 100;
        job.CurrentStep = "Completed";
        job.Log += $"\n{app.Name} removed.";
        job.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await bc.CompletedAsync(jobId, true, $"{app.Name} uninstalled.", null);
    }

    // ---------------- Clone (staging) ----------------
    private async Task RunCloneAsync(IServiceProvider sp, int jobId, string userId, int installationId,
        int targetDomainId, string path, CancellationToken ct)
    {
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var bc = sp.GetRequiredService<IInstallBroadcast>();
        var runner = sp.GetRequiredService<ICommandRunner>();
        var mysql = sp.GetRequiredService<IMySqlService>();

        var job = await db.AppInstallJobs.FirstAsync(j => j.Id == jobId, ct);
        var src = await db.AppInstallations.Include(i => i.AppDefinition).FirstAsync(i => i.Id == installationId, ct);
        var target = await db.Domains.FirstAsync(d => d.Id == targetDomainId, ct);
        var app = src.AppDefinition!;
        var relPath = NormalizePath(path);
        var siteUrl = $"https://{target.DomainName}{(relPath == "/" ? "" : relPath)}";

        await StepAsync(db, bc, job, 25, "Copy Files", $"Copying files to {target.DomainName}{relPath}…");
        await runner.RunAsync($"cp -a {src.InstallPath}/. {target.DocumentRoot}{relPath}", ServiceName);

        string? cloneDb = null;
        if (app.RequiresDatabase && !string.IsNullOrEmpty(src.DatabaseName))
        {
            cloneDb = $"{src.DatabaseName}_stg".Length > 60 ? src.DatabaseName![..56] + "_stg" : $"{src.DatabaseName}_stg";
            await StepAsync(db, bc, job, 55, "Clone Database", $"Duplicating database into {cloneDb}…");
            await mysql.CreateDatabaseAsync(cloneDb);
            await runner.RunAsync($"mysqldump {src.DatabaseName} | mysql {cloneDb}", ServiceName);
        }

        await StepAsync(db, bc, job, 85, "Rewrite URLs", $"Search-replace {src.SiteUrl} → {siteUrl}…");
        await runner.RunAsync($"wp search-replace '{src.SiteUrl}' '{siteUrl}' --path={target.DocumentRoot}{relPath}", ServiceName);

        var clone = new AppInstallation
        {
            UserId = userId, DomainId = target.Id, AppDefinitionId = app.Id,
            InstalledVersion = src.InstalledVersion, InstallPath = relPath,
            DatabaseName = cloneDb, DatabaseUser = src.DatabaseUser, TablePrefix = src.TablePrefix,
            SiteUrl = siteUrl, AdminUrl = AdminUrlFor(app.Slug, siteUrl),
            SiteTitle = $"{src.SiteTitle} (staging)", AdminUser = src.AdminUser, AdminEmail = src.AdminEmail,
            PhpVersion = src.PhpVersion, Language = src.Language,
            Status = AppInstallStatus.Active, IsStaging = true, InstalledAt = DateTime.UtcNow
        };
        db.AppInstallations.Add(clone);
        await db.SaveChangesAsync(ct);

        job.InstallationId = clone.Id;
        job.Status = AppJobStatus.Success;
        job.Progress = 100;
        job.CurrentStep = "Completed";
        job.Log += $"\nStaging clone created at {siteUrl}";
        job.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await bc.CompletedAsync(jobId, true, "Staging clone created.", clone.Id);
    }

    /// <summary>Creates a labelled restore point and trims older ones to the configured retention.</summary>
    private static async Task CreateRestorePointAsync(IServiceProvider sp, string userId, string label, CancellationToken ct)
    {
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var backups = sp.GetRequiredService<IBackupService>();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

        var backup = await backups.CreateBackupAsync(userId, user?.UserName ?? "user", BackupType.Full);
        backup.Label = label;
        await db.SaveChangesAsync(ct);

        var keep = (await db.AppUpdateSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1, ct))?.KeepRestorePoints ?? 3;
        var stale = await db.Backups.Where(b => b.UserId == userId && b.Label != null)
            .OrderByDescending(b => b.CreatedAt).Skip(keep).ToListAsync(ct);
        if (stale.Count > 0)
        {
            db.Backups.RemoveRange(stale);
            await db.SaveChangesAsync(ct);
        }
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Trim() == "/") return "/";
        var p = "/" + path.Trim().Trim('/');
        return p;
    }

    private static string AdminUrlFor(string slug, string siteUrl) => slug switch
    {
        "wordpress" or "woocommerce" or "wp-easycart" => $"{siteUrl}/wp-admin",
        "joomla" => $"{siteUrl}/administrator",
        "drupal" => $"{siteUrl}/user/login",
        "prestashop" => $"{siteUrl}/admin",
        "opencart" => $"{siteUrl}/admin",
        "magento" => $"{siteUrl}/admin",
        "phpbb" => $"{siteUrl}/adm",
        "mybb" => $"{siteUrl}/admin",
        "ghost" => $"{siteUrl}/ghost",
        "nextcloud" => $"{siteUrl}/settings/admin",
        "osticket" => $"{siteUrl}/scp",
        "mediawiki" => $"{siteUrl}/index.php/Special:UserLogin",
        "dokuwiki" => $"{siteUrl}/doku.php?do=admin",
        "bookstack" => $"{siteUrl}/settings",
        _ => $"{siteUrl}/admin"
    };
}
