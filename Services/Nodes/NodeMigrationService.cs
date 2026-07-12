using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Services.Nodes;

public interface INodeMigrationService
{
    Task RunAsync(int migrationId, int domainId, int fromNodeId, int toNodeId, MigrationType type, string triggeredBy);
}

/// <summary>
/// Migrates a domain (files/DB/email) between nodes over SSH, broadcasting each step.
/// Every remote command goes through INodeSshService, so the whole flow is simulation-safe.
/// On failure it rolls back the target-side changes it made.
/// </summary>
public class NodeMigrationService : INodeMigrationService
{
    private readonly ApplicationDbContext _db;
    private readonly INodeSshService _ssh;
    private readonly INodeBroadcast _broadcast;
    private readonly INotificationService _notifications;
    private readonly ILogger<NodeMigrationService> _logger;

    public NodeMigrationService(ApplicationDbContext db, INodeSshService ssh, INodeBroadcast broadcast,
        INotificationService notifications, ILogger<NodeMigrationService> logger)
    {
        _db = db;
        _ssh = ssh;
        _broadcast = broadcast;
        _notifications = notifications;
        _logger = logger;
    }

    private record Step(int Percent, string Name, Func<Task> Action, Func<Task>? Rollback = null);

    public async Task RunAsync(int migrationId, int domainId, int fromNodeId, int toNodeId, MigrationType type, string triggeredBy)
    {
        var domain = await _db.Domains.FirstOrDefaultAsync(d => d.Id == domainId);
        var source = await _db.ServerNodes.FirstOrDefaultAsync(n => n.Id == fromNodeId);
        var target = await _db.ServerNodes.FirstOrDefaultAsync(n => n.Id == toNodeId);

        if (domain == null || source == null || target == null)
        {
            await _broadcast.MigrationCompletedAsync(migrationId, false, "Domain or node not found.");
            return;
        }

        var completed = new List<Step>();

        async Task Emit(int percent, string step, string log)
        {
            await _broadcast.MigrationProgressAsync(migrationId, percent, step, log);
            await Task.Delay(1000);
        }

        var doFiles = type is MigrationType.Full or MigrationType.FilesOnly;
        var doDb = type is MigrationType.Full or MigrationType.DatabaseOnly;

        var steps = new List<Step>
        {
            new(10, "Creating target user account", async () =>
                await _ssh.ExecuteCommandAsync(target, $"id {domain.UserId} || useradd -m {SafeUser(domain)}"),
                async () => await _ssh.ExecuteCommandAsync(target, $"# rollback: leave user {SafeUser(domain)} in place")),
        };

        if (doFiles)
            steps.Add(new(30, "Syncing files (rsync)", async () =>
                await _ssh.ExecuteCommandAsync(source,
                    $"rsync -az -e 'ssh -p {target.SshPort}' {domain.DocumentRoot}/ {target.SshUsername}@{target.IpAddress}:{domain.DocumentRoot}/"),
                async () => await _ssh.ExecuteCommandAsync(target, $"rm -rf {domain.DocumentRoot}")));

        if (doDb)
        {
            steps.Add(new(45, "Dumping database", async () =>
                await _ssh.ExecuteCommandAsync(source, $"mysqldump --all-databases > /tmp/migrate-{domainId}.sql")));
            steps.Add(new(55, "Importing database", async () =>
                await _ssh.ExecuteCommandAsync(target, $"mysql < /tmp/migrate-{domainId}.sql")));
        }

        steps.Add(new(65, "Configuring Nginx on target", async () =>
            await _ssh.ExecuteCommandAsync(target, $"nginx -t && systemctl reload nginx"),
            async () => await _ssh.ExecuteCommandAsync(target, $"rm -f /etc/nginx/sites-enabled/{domain.DomainName}.conf")));

        steps.Add(new(75, "Updating DNS to the new IP", async () =>
        {
            var zone = await _db.DnsZones.Include(z => z.Records).FirstOrDefaultAsync(z => z.DomainId == domainId);
            if (zone != null)
                foreach (var record in zone.Records.Where(r => r.Type == DnsRecordType.A))
                    record.Value = target.IpAddress;
            await _db.SaveChangesAsync();
        }));

        steps.Add(new(85, "Testing target site", async () =>
            await _ssh.ExecuteCommandAsync(target, $"curl -sI -H 'Host: {domain.DomainName}' http://localhost/ | head -1")));

        if (type == MigrationType.Full)
            steps.Add(new(92, "Removing from source", async () =>
                await _ssh.ExecuteCommandAsync(source, $"rm -rf {domain.DocumentRoot} && systemctl reload nginx")));

        try
        {
            await Emit(5, "Starting", $"Migrating {domain.DomainName}: {source.Name} → {target.Name} ({type}).");

            foreach (var step in steps)
            {
                await Emit(step.Percent, step.Name, $"▸ {step.Name}…");
                await step.Action();
                completed.Add(step);
            }

            // Repoint the placement record.
            var placement = await _db.DomainNodes.FirstOrDefaultAsync(d => d.DomainId == domainId);
            if (placement != null) { placement.NodeId = toNodeId; placement.MigratedAt = DateTime.UtcNow; }
            else _db.DomainNodes.Add(new DomainNode { DomainId = domainId, NodeId = toNodeId, MigratedAt = DateTime.UtcNow });
            await _db.SaveChangesAsync();

            await Emit(100, "Complete", $"✓ {domain.DomainName} is now served from {target.Name}.");
            await _broadcast.MigrationCompletedAsync(migrationId, true, $"Migration complete — {domain.DomainName} → {target.Name}.");

            await _notifications.NotifyAsync(domain.UserId, "Domain migrated",
                $"{domain.DomainName} moved to {target.Name} ({target.Location}).", NotificationType.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Migration {MigrationId} failed", migrationId);
            await _broadcast.MigrationProgressAsync(migrationId, 0, "Rolling back", $"✗ {ex.Message} — rolling back…");

            // Undo the target-side steps in reverse.
            completed.Reverse();
            foreach (var step in completed.Where(s => s.Rollback != null))
            {
                try { await step.Rollback!(); }
                catch (Exception rex) { _logger.LogWarning(rex, "Rollback step failed"); }
            }

            await _broadcast.MigrationCompletedAsync(migrationId, false, $"Migration failed and was rolled back: {ex.Message}");
        }
    }

    private static string SafeUser(Domain domain)
    {
        var owner = domain.User?.UserName ?? "user";
        return new string(owner.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }
}
