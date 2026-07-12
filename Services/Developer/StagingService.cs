using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Developer;

public record StagingOptions(bool CloneDatabase, bool PasswordProtect, string? AuthUser, string? AuthPassword, int? ExpiryDays);

public record PushOptions(bool SyncFiles, bool SyncDatabase, bool ExcludeUploads, bool ClearCaches);

public interface IStagingService
{
    Task<List<StagingSite>> GetSitesAsync(string userId);
    Task<StagingSite?> GetSiteAsync(string userId, int id);
    Task<StagingSite?> GetSiteForDomainAsync(string userId, int domainId);

    /// <summary>Creates staging.{domain} and clones production into it.</summary>
    Task<ServiceResult> CreateAsync(string userId, int domainId, StagingOptions options);

    /// <summary>Re-clones production over the staging site.</summary>
    Task<ServiceResult> RefreshAsync(string userId, int stagingId);

    /// <summary>Pushes staging back to production.</summary>
    Task<ServiceResult> PushToProductionAsync(string userId, int stagingId, PushOptions options);

    Task<ServiceResult> DeleteAsync(string userId, int stagingId);
    Task SetPasswordProtectionAsync(string userId, int stagingId, bool enabled, string? user, string? password);
    Task SetExpiryAsync(string userId, int stagingId, int? days);

    /// <summary>Deletes staging sites past their expiry. Returns how many were removed.</summary>
    Task<int> ReapExpiredAsync();
}

/// <summary>
/// Clones a production site into a staging subdomain and pushes changes back.
/// All filesystem and database work goes through the integration services, so the whole
/// flow is simulation-safe: on a dev host the commands are recorded, not executed.
/// </summary>
public class StagingService : IStagingService
{
    private const string ServiceName = "staging";

    private readonly ApplicationDbContext _db;
    private readonly ICommandRunner _runner;
    private readonly IMySqlService _mysql;
    private readonly INginxService _nginx;
    private readonly ISecretHasher _hasher;
    private readonly INotificationService _notifications;

    public StagingService(ApplicationDbContext db, ICommandRunner runner, IMySqlService mysql,
        INginxService nginx, ISecretHasher hasher, INotificationService notifications)
    {
        _db = db;
        _runner = runner;
        _mysql = mysql;
        _nginx = nginx;
        _hasher = hasher;
        _notifications = notifications;
    }

    public Task<List<StagingSite>> GetSitesAsync(string userId) =>
        _db.StagingSites.Include(s => s.Domain).Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt).ToListAsync();

    public Task<StagingSite?> GetSiteAsync(string userId, int id) =>
        _db.StagingSites.Include(s => s.Domain).FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);

    public Task<StagingSite?> GetSiteForDomainAsync(string userId, int domainId) =>
        _db.StagingSites.Include(s => s.Domain).FirstOrDefaultAsync(s => s.DomainId == domainId && s.UserId == userId);

    public async Task<ServiceResult> CreateAsync(string userId, int domainId, StagingOptions options)
    {
        var domain = await _db.Domains.FirstOrDefaultAsync(d => d.Id == domainId && d.UserId == userId)
            ?? throw new InvalidOperationException("Domain not found.");

        if (await _db.StagingSites.AnyAsync(s => s.DomainId == domainId))
            throw new InvalidOperationException($"{domain.DomainName} already has a staging site.");

        if (options.PasswordProtect && (string.IsNullOrWhiteSpace(options.AuthUser) || string.IsNullOrWhiteSpace(options.AuthPassword)))
            throw new InvalidOperationException("Set a username and password for the staging site's basic auth.");

        var stagingDomain = $"staging.{domain.DomainName}";
        var stagingPath = $"{domain.DocumentRoot.TrimEnd('/')}-staging";

        var site = new StagingSite
        {
            UserId = userId,
            DomainId = domain.Id,
            StagingDomain = stagingDomain,
            StagingPath = stagingPath,
            PasswordProtected = options.PasswordProtect,
            AuthUser = options.AuthUser,
            AuthPasswordHash = options.PasswordProtect ? _hasher.Hash(options.AuthPassword!) : null,
            ExpiresAt = options.ExpiryDays is int days and > 0 ? DateTime.UtcNow.AddDays(days) : null,
            Status = StagingStatus.Creating,
            CreatedAt = DateTime.UtcNow
        };

        _db.StagingSites.Add(site);
        await _db.SaveChangesAsync();

        var commands = new List<CommandResult>();

        try
        {
            // 1. Subdomain + vhost.
            if (!await _db.Subdomains.AnyAsync(s => s.DomainId == domain.Id && s.Name == "staging"))
            {
                _db.Subdomains.Add(new Subdomain
                {
                    DomainId = domain.Id, UserId = userId, Name = "staging",
                    DocumentRoot = stagingPath, CreatedAt = DateTime.UtcNow
                });
            }

            commands.Add(await _runner.RunAsync($"mkdir -p {stagingPath}", ServiceName));

            // 2. Copy files.
            commands.Add(await _runner.RunAsync($"rsync -a --delete {domain.DocumentRoot}/ {stagingPath}/", ServiceName));

            // 3. Clone the database with a staging prefix.
            if (options.CloneDatabase)
            {
                var production = await _db.Databases.FirstOrDefaultAsync(d => d.DomainId == domain.Id && d.UserId == userId);
                if (production != null)
                {
                    var stagingDb = Shorten($"{production.DbName}_stg");
                    site.DatabaseName = stagingDb;
                    site.TablePrefix = "stg_";

                    commands.AddRange((await _mysql.CreateDatabaseAsync(stagingDb)).Commands);
                    commands.Add(await _runner.RunAsync($"mysqldump {production.DbName} | mysql {stagingDb}", ServiceName));

                    if (!string.IsNullOrEmpty(production.DbUser))
                        commands.AddRange((await _mysql.GrantPermissionsAsync(stagingDb, production.DbUser)).Commands);
                }
            }

            // 4. Rewrite site URLs and force staging mode for WordPress installs.
            var isWordPress = await _db.AppInstallations
                .Include(i => i.AppDefinition)
                .AnyAsync(i => i.DomainId == domain.Id &&
                               (i.AppDefinition!.Slug == "wordpress" || i.AppDefinition.Slug == "woocommerce"));

            if (isWordPress)
            {
                commands.Add(await _runner.RunAsync(
                    $"wp option update home 'https://{stagingDomain}' --path={stagingPath}", ServiceName));
                commands.Add(await _runner.RunAsync(
                    $"wp option update siteurl 'https://{stagingDomain}' --path={stagingPath}", ServiceName));
                commands.Add(await _runner.RunAsync(
                    $"wp search-replace 'https://{domain.DomainName}' 'https://{stagingDomain}' --path={stagingPath} --skip-columns=guid", ServiceName));
            }

            // 5. A staging site must never send mail or take a payment.
            commands.Add(await _runner.WriteFileAsync($"{stagingPath}/.srx-staging",
                $"STAGING=1\nDISABLE_EMAILS=1\nDISABLE_PAYMENTS=1\nCLONED_FROM={domain.DomainName}\nCREATED={DateTime.UtcNow:u}\n",
                ServiceName));

            // 6. Basic auth.
            if (options.PasswordProtect)
                commands.Add(await WriteHtpasswdAsync(stagingPath, options.AuthUser!, options.AuthPassword!));

            // 7. Serve it.
            var vhost = await _nginx.CreateVirtualHostAsync(stagingDomain, stagingPath, domain.PhpVersion);
            commands.AddRange(vhost.Commands);

            site.Status = StagingStatus.Active;
            site.LastSyncAt = DateTime.UtcNow;
            site.LastSyncDirection = "clone";
            await _db.SaveChangesAsync();

            await _notifications.NotifyAsync(userId, "Staging site ready",
                $"https://{stagingDomain} is a copy of {domain.DomainName}.", NotificationType.Success);

            return ServiceResult.Ok($"Staging site created at {stagingDomain}.", commands.ToArray());
        }
        catch (Exception ex)
        {
            site.Status = StagingStatus.Failed;
            await _db.SaveChangesAsync();
            return ServiceResult.Fail($"Could not create the staging site: {ex.Message}", commands.ToArray());
        }
    }

    public async Task<ServiceResult> RefreshAsync(string userId, int stagingId)
    {
        var site = await GetSiteAsync(userId, stagingId) ?? throw new InvalidOperationException("Staging site not found.");
        var domain = site.Domain ?? throw new InvalidOperationException("Domain not found.");

        site.Status = StagingStatus.Syncing;
        await _db.SaveChangesAsync();

        var commands = new List<CommandResult>
        {
            await _runner.RunAsync($"rsync -a --delete {domain.DocumentRoot}/ {site.StagingPath}/", ServiceName)
        };

        if (site.DatabaseName != null)
        {
            var production = await _db.Databases.FirstOrDefaultAsync(d => d.DomainId == domain.Id && d.UserId == userId);
            if (production != null)
                commands.Add(await _runner.RunAsync($"mysqldump {production.DbName} | mysql {site.DatabaseName}", ServiceName));
        }

        commands.Add(await _runner.RunAsync(
            $"wp search-replace 'https://{domain.DomainName}' 'https://{site.StagingDomain}' --path={site.StagingPath} --skip-columns=guid", ServiceName));

        site.Status = StagingStatus.Active;
        site.LastSyncAt = DateTime.UtcNow;
        site.LastSyncDirection = "clone";
        await _db.SaveChangesAsync();

        return ServiceResult.Ok($"Staging refreshed from {domain.DomainName}.", commands.ToArray());
    }

    public async Task<ServiceResult> PushToProductionAsync(string userId, int stagingId, PushOptions options)
    {
        var site = await GetSiteAsync(userId, stagingId) ?? throw new InvalidOperationException("Staging site not found.");
        var domain = site.Domain ?? throw new InvalidOperationException("Domain not found.");

        if (!options.SyncFiles && !options.SyncDatabase)
            throw new InvalidOperationException("Choose files, the database, or both.");

        site.Status = StagingStatus.Syncing;
        await _db.SaveChangesAsync();

        var commands = new List<CommandResult>();

        // Always take a rollback point before overwriting production.
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        commands.Add(await _runner.RunAsync(
            $"tar -czf /home/backups/{domain.DomainName}-pre-push-{stamp}.tar.gz -C {domain.DocumentRoot} .", ServiceName));

        if (options.SyncFiles)
        {
            var exclude = options.ExcludeUploads
                ? "--exclude='wp-content/uploads/' --exclude='storage/app/public/' --exclude='public/uploads/' "
                : "";
            commands.Add(await _runner.RunAsync(
                $"rsync -a --delete {exclude}--exclude='.srx-staging' --exclude='.htpasswd' {site.StagingPath}/ {domain.DocumentRoot}/", ServiceName));
        }

        if (options.SyncDatabase && site.DatabaseName != null)
        {
            var production = await _db.Databases.FirstOrDefaultAsync(d => d.DomainId == domain.Id && d.UserId == userId);
            if (production != null)
            {
                commands.Add(await _runner.RunAsync(
                    $"mysqldump {production.DbName} > /home/backups/{production.DbName}-pre-push-{stamp}.sql", ServiceName));
                commands.Add(await _runner.RunAsync($"mysqldump {site.DatabaseName} | mysql {production.DbName}", ServiceName));
                commands.Add(await _runner.RunAsync(
                    $"wp search-replace 'https://{site.StagingDomain}' 'https://{domain.DomainName}' --path={domain.DocumentRoot} --skip-columns=guid", ServiceName));
            }
        }

        if (options.ClearCaches)
        {
            commands.Add(await _runner.RunAsync($"wp cache flush --path={domain.DocumentRoot}", ServiceName));
            commands.Add(await _runner.RunAsync($"rm -rf {domain.DocumentRoot}/wp-content/cache/*", ServiceName));
            commands.Add(await _runner.RunAsync("systemctl reload php8.3-fpm || true", ServiceName));
        }

        site.Status = StagingStatus.Active;
        site.LastSyncAt = DateTime.UtcNow;
        site.LastSyncDirection = "push";
        await _db.SaveChangesAsync();

        await _notifications.NotifyAsync(userId, "Staging pushed to production",
            $"{site.StagingDomain} was pushed to {domain.DomainName}. A rollback archive was taken first.",
            NotificationType.Warning);

        return ServiceResult.Ok($"Staging pushed to {domain.DomainName}. A pre-push backup was taken.", commands.ToArray());
    }

    public async Task<ServiceResult> DeleteAsync(string userId, int stagingId)
    {
        var site = await GetSiteAsync(userId, stagingId) ?? throw new InvalidOperationException("Staging site not found.");

        var commands = new List<CommandResult>();
        var vhost = await _nginx.DeleteVirtualHostAsync(site.StagingDomain);
        commands.AddRange(vhost.Commands);

        commands.Add(await _runner.DeletePathAsync(site.StagingPath, ServiceName));

        if (site.DatabaseName != null)
            commands.AddRange((await _mysql.DeleteDatabaseAsync(site.DatabaseName)).Commands);

        var subdomain = await _db.Subdomains.FirstOrDefaultAsync(s => s.DomainId == site.DomainId && s.Name == "staging");
        if (subdomain != null) _db.Subdomains.Remove(subdomain);

        _db.StagingSites.Remove(site);
        await _db.SaveChangesAsync();

        return ServiceResult.Ok($"Staging site {site.StagingDomain} deleted.", commands.ToArray());
    }

    public async Task SetPasswordProtectionAsync(string userId, int stagingId, bool enabled, string? user, string? password)
    {
        var site = await GetSiteAsync(userId, stagingId) ?? throw new InvalidOperationException("Staging site not found.");

        if (enabled)
        {
            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("Set a username and password.");

            site.AuthUser = user;
            site.AuthPasswordHash = _hasher.Hash(password);
            site.PasswordProtected = true;
            await WriteHtpasswdAsync(site.StagingPath, user, password);
        }
        else
        {
            site.PasswordProtected = false;
            await _runner.DeleteFileAsync($"{site.StagingPath}/.htpasswd", ServiceName);
        }

        await _db.SaveChangesAsync();
    }

    public async Task SetExpiryAsync(string userId, int stagingId, int? days)
    {
        var site = await GetSiteAsync(userId, stagingId) ?? throw new InvalidOperationException("Staging site not found.");
        site.ExpiresAt = days is int d and > 0 ? DateTime.UtcNow.AddDays(d) : null;
        await _db.SaveChangesAsync();
    }

    public async Task<int> ReapExpiredAsync()
    {
        var now = DateTime.UtcNow;
        var expired = await _db.StagingSites.Where(s => s.ExpiresAt != null && s.ExpiresAt <= now).ToListAsync();

        foreach (var site in expired)
        {
            await DeleteAsync(site.UserId, site.Id);
            await _notifications.NotifyAsync(site.UserId, "Staging site expired",
                $"{site.StagingDomain} reached its expiry date and was removed.", NotificationType.Info);
        }

        return expired.Count;
    }

    /// <summary>Writes the .htpasswd nginx reads for basic auth. The panel stores only a BCrypt hash.</summary>
    private async Task<CommandResult> WriteHtpasswdAsync(string stagingPath, string user, string password)
    {
        // htpasswd -bB writes bcrypt, which is what our stored hash already is.
        var hash = _hasher.Hash(password);
        return await _runner.WriteFileAsync($"{stagingPath}/.htpasswd", $"{user}:{hash}\n", ServiceName);
    }

    private static string Shorten(string name) => name.Length <= 64 ? name : name[..60] + "_stg";
}
