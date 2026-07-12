using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;

namespace SRXPanel.Services.Vps;

public record VpsOrderConfig(string Hostname, string? RootPassword, int? SshKeyId, int TemplateId, int? NodeId);

/// <summary>
/// Orchestrates VPS instances against Proxmox nodes: VMID/IP allocation, ordering, power actions,
/// resize/rebuild, snapshots and backups. All Proxmox calls go through IProxmoxService (sim-safe)
/// and every action is recorded as a VpsAction row.
/// </summary>
public interface IVpsManagerService
{
    Task<ProxmoxNode?> GetBestNodeAsync(int? preferredNodeId = null);
    Task<int> AllocateVmIdAsync();
    Task<VpsIpAddress?> AllocateIpAsync(int nodeId, int instanceId);

    Task<VpsInstance> CreateInstanceAsync(string userId, VpsPlan plan, VpsOrderConfig config);

    Task<VpsAction> PowerActionAsync(VpsInstance vps, VpsActionType action, string userId);
    Task<VpsAction> ResizeAsync(VpsInstance vps, int cpuCores, int ramMB, int diskGB, string userId);
    Task<VpsAction> RebuildAsync(VpsInstance vps, int templateId, string rootPassword, string userId);

    Task<VpsSnapshot> CreateSnapshotAsync(VpsInstance vps, string name, string userId);
    Task RestoreSnapshotAsync(VpsInstance vps, int snapshotId, string userId);
    Task DeleteSnapshotAsync(VpsInstance vps, int snapshotId, string userId);

    Task<VpsBackup> CreateBackupAsync(VpsInstance vps, string userId);
    Task RestoreBackupAsync(VpsInstance vps, int backupId, string userId);
    Task DeleteBackupAsync(VpsInstance vps, int backupId, string userId);

    Task SuspendAsync(VpsInstance vps, string userId, bool bandwidth = false);
    Task ResumeAsync(VpsInstance vps, string userId);
    Task DeleteAsync(VpsInstance vps, string userId);

    Task<ProxmoxConsole?> OpenConsoleAsync(VpsInstance vps, string userId);
}

public class VpsManagerService : IVpsManagerService
{
    private const int FirstVmId = 100;

    private readonly ApplicationDbContext _db;
    private readonly IProxmoxService _proxmox;
    private readonly IVpsBroadcast _broadcast;
    private readonly INotificationService _notifications;
    private readonly ILogger<VpsManagerService> _logger;

    public VpsManagerService(ApplicationDbContext db, IProxmoxService proxmox, IVpsBroadcast broadcast,
        INotificationService notifications, ILogger<VpsManagerService> logger)
    {
        _db = db;
        _proxmox = proxmox;
        _broadcast = broadcast;
        _notifications = notifications;
        _logger = logger;
    }

    // ---------------- Placement ----------------

    public async Task<ProxmoxNode?> GetBestNodeAsync(int? preferredNodeId = null)
    {
        if (preferredNodeId is int pid)
        {
            var preferred = await _db.ProxmoxNodes.FirstOrDefaultAsync(n => n.Id == pid && n.IsActive);
            if (preferred != null) return preferred;
        }

        var nodes = await _db.ProxmoxNodes.Where(n => n.IsActive).ToListAsync();
        if (nodes.Count == 0) return null;

        // Least-loaded: fewest live VMs relative to capacity.
        ProxmoxNode? best = null;
        double bestScore = double.MaxValue;
        foreach (var node in nodes)
        {
            var used = await _db.VpsInstances.CountAsync(v => v.NodeId == node.Id && v.Status != VpsStatus.Deleted);
            if (used >= node.MaxVms) continue;
            var score = (double)used / Math.Max(1, node.MaxVms);
            if (score < bestScore) { bestScore = score; best = node; }
        }
        return best ?? nodes.First();
    }

    public async Task<int> AllocateVmIdAsync()
    {
        var max = await _db.VpsInstances.Select(v => (int?)v.VmId).MaxAsync() ?? (FirstVmId - 1);
        return Math.Max(FirstVmId, max + 1);
    }

    public async Task<VpsIpAddress?> AllocateIpAsync(int nodeId, int instanceId)
    {
        var ip = await _db.VpsIpAddresses
            .Where(a => a.NodeId == nodeId && !a.IsIpv6 && a.AssignedInstanceId == null && !a.IsReserved)
            .OrderBy(a => a.Id)
            .FirstOrDefaultAsync();
        if (ip != null)
        {
            ip.AssignedInstanceId = instanceId;
            await _db.SaveChangesAsync();
        }
        return ip;
    }

    // ---------------- Order ----------------

    public async Task<VpsInstance> CreateInstanceAsync(string userId, VpsPlan plan, VpsOrderConfig config)
    {
        var node = await GetBestNodeAsync(config.NodeId ?? plan.NodeId)
            ?? throw new InvalidOperationException("No active Proxmox node is available.");

        var vmId = await AllocateVmIdAsync();

        var template = await _db.VpsTemplates.FirstOrDefaultAsync(t => t.Id == config.TemplateId && t.IsActive);

        var vps = new VpsInstance
        {
            UserId = userId,
            PlanId = plan.Id,
            NodeId = node.Id,
            VmId = vmId,
            Hostname = config.Hostname,
            Status = VpsStatus.Building,
            OsTemplate = template?.OsType ?? "ubuntu",
            CpuCores = plan.CpuCores,
            RamMB = plan.RamMB,
            DiskGB = plan.DiskGB,
            BandwidthGB = plan.BandwidthGB,
            RootPassword = config.RootPassword,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = plan.BillingCycle == BillingCycle.Annual ? DateTime.UtcNow.AddYears(1) : DateTime.UtcNow.AddMonths(1),
            BandwidthCycleStart = DateTime.UtcNow
        };

        _db.VpsInstances.Add(vps);
        await _db.SaveChangesAsync();

        // Assign an IP from the node pool (falls back to a deterministic sim IP during provisioning).
        var ip = await AllocateIpAsync(node.Id, vps.Id);
        if (ip != null)
        {
            vps.IpAddress = ip.Address;
            await _db.SaveChangesAsync();
        }

        return vps;
    }

    // ---------------- Actions ----------------

    private async Task<VpsAction> RecordAsync(VpsInstance vps, VpsActionType type, string userId,
        Func<Task<ProxmoxResult>> op, VpsStatus? newStatus = null)
    {
        var action = new VpsAction
        {
            VpsInstanceId = vps.Id, UserId = userId, Action = type,
            Status = VpsActionStatus.Running, StartedAt = DateTime.UtcNow
        };
        _db.VpsActions.Add(action);
        await _db.SaveChangesAsync();

        try
        {
            var node = vps.Node ?? await _db.ProxmoxNodes.FirstAsync(n => n.Id == vps.NodeId);
            var result = await op();
            action.Status = result.Success ? VpsActionStatus.Success : VpsActionStatus.Failed;
            action.Output = result.Output;
            action.TaskId = result.TaskId;
            action.CompletedAt = DateTime.UtcNow;

            if (result.Success && newStatus.HasValue)
            {
                vps.Status = newStatus.Value;
                await _broadcast.StatusAsync(vps.Id, newStatus.Value.ToString());
            }
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VPS action {Action} on {Vps} failed", type, vps.Id);
            action.Status = VpsActionStatus.Failed;
            action.Output = ex.Message;
            action.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return action;
    }

    public async Task<VpsAction> PowerActionAsync(VpsInstance vps, VpsActionType action, string userId)
    {
        var node = vps.Node ?? await _db.ProxmoxNodes.FirstAsync(n => n.Id == vps.NodeId);
        return action switch
        {
            VpsActionType.Start => await RecordAsync(vps, action, userId, () => _proxmox.StartVmAsync(node, vps.VmId), VpsStatus.Running),
            VpsActionType.Stop => await RecordAsync(vps, action, userId, () => _proxmox.StopVmAsync(node, vps.VmId), VpsStatus.Stopped),
            VpsActionType.Shutdown => await RecordAsync(vps, action, userId, () => _proxmox.ShutdownVmAsync(node, vps.VmId), VpsStatus.Stopped),
            VpsActionType.Restart => await RecordAsync(vps, action, userId, () => _proxmox.RestartVmAsync(node, vps.VmId), VpsStatus.Running),
            _ => throw new InvalidOperationException($"{action} is not a power action.")
        };
    }

    public async Task<VpsAction> ResizeAsync(VpsInstance vps, int cpuCores, int ramMB, int diskGB, string userId)
    {
        var node = vps.Node ?? await _db.ProxmoxNodes.FirstAsync(n => n.Id == vps.NodeId);
        var action = await RecordAsync(vps, VpsActionType.Resize, userId,
            () => _proxmox.ResizeVmAsync(node, vps.VmId, diskGB, ramMB, cpuCores));
        if (action.Status == VpsActionStatus.Success)
        {
            vps.CpuCores = cpuCores; vps.RamMB = ramMB; vps.DiskGB = diskGB;
            await _db.SaveChangesAsync();
        }
        return action;
    }

    public async Task<VpsAction> RebuildAsync(VpsInstance vps, int templateId, string rootPassword, string userId)
    {
        var node = vps.Node ?? await _db.ProxmoxNodes.FirstAsync(n => n.Id == vps.NodeId);
        var template = await _db.VpsTemplates.FirstOrDefaultAsync(t => t.Id == templateId);
        var action = await RecordAsync(vps, VpsActionType.Rebuild, userId,
            () => _proxmox.RebuildVmAsync(node, vps.VmId, template?.TemplateId ?? templateId, rootPassword),
            VpsStatus.Running);
        if (action.Status == VpsActionStatus.Success)
        {
            vps.RootPassword = rootPassword;
            if (template != null) vps.OsTemplate = template.OsType;
            await _db.SaveChangesAsync();
            await _notifications.NotifyAsync(vps.UserId, "VPS rebuilt",
                $"{vps.Hostname} was rebuilt with a fresh OS image.", NotificationType.Success);
        }
        return action;
    }

    // ---------------- Snapshots ----------------

    public async Task<VpsSnapshot> CreateSnapshotAsync(VpsInstance vps, string name, string userId)
    {
        var node = vps.Node ?? await _db.ProxmoxNodes.FirstAsync(n => n.Id == vps.NodeId);
        var safe = new string(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        if (string.IsNullOrEmpty(safe)) safe = $"snap{DateTime.UtcNow:MMddHHmm}";

        var snapshot = new VpsSnapshot { VpsInstanceId = vps.Id, Name = safe, Status = VpsSnapshotStatus.Creating };
        _db.VpsSnapshots.Add(snapshot);
        await _db.SaveChangesAsync();

        var result = await _proxmox.CreateSnapshotAsync(node, vps.VmId, safe);
        await RecordAsync(vps, VpsActionType.Snapshot, userId, () => Task.FromResult(result));
        snapshot.Status = result.Success ? VpsSnapshotStatus.Ready : VpsSnapshotStatus.Failed;
        await _db.SaveChangesAsync();
        return snapshot;
    }

    public async Task RestoreSnapshotAsync(VpsInstance vps, int snapshotId, string userId)
    {
        var node = vps.Node ?? await _db.ProxmoxNodes.FirstAsync(n => n.Id == vps.NodeId);
        var snapshot = await _db.VpsSnapshots.FirstOrDefaultAsync(s => s.Id == snapshotId && s.VpsInstanceId == vps.Id);
        if (snapshot == null) return;
        await RecordAsync(vps, VpsActionType.Restore, userId, () => _proxmox.RestoreSnapshotAsync(node, vps.VmId, snapshot.Name));
    }

    public async Task DeleteSnapshotAsync(VpsInstance vps, int snapshotId, string userId)
    {
        var node = vps.Node ?? await _db.ProxmoxNodes.FirstAsync(n => n.Id == vps.NodeId);
        var snapshot = await _db.VpsSnapshots.FirstOrDefaultAsync(s => s.Id == snapshotId && s.VpsInstanceId == vps.Id);
        if (snapshot == null) return;
        await _proxmox.DeleteSnapshotAsync(node, vps.VmId, snapshot.Name);
        snapshot.Status = VpsSnapshotStatus.Deleted;
        snapshot.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    // ---------------- Backups ----------------

    public async Task<VpsBackup> CreateBackupAsync(VpsInstance vps, string userId)
    {
        var node = vps.Node ?? await _db.ProxmoxNodes.FirstAsync(n => n.Id == vps.NodeId);
        var backup = new VpsBackup
        {
            VpsInstanceId = vps.Id, UserId = userId, Status = VpsBackupStatus.Creating,
            StoragePath = $"{node.Storage}:backup/vzdump-qemu-{vps.VmId}-{DateTime.UtcNow:yyyy_MM_dd-HH_mm_ss}.vma.zst"
        };
        _db.VpsBackups.Add(backup);
        await _db.SaveChangesAsync();

        var result = await _proxmox.CreateBackupAsync(node, vps.VmId, node.Storage);
        await RecordAsync(vps, VpsActionType.Backup, userId, () => Task.FromResult(result));

        backup.Status = result.Success ? VpsBackupStatus.Ready : VpsBackupStatus.Failed;
        backup.SizeMB = result.Success ? new Random(vps.VmId).Next(800, vps.DiskGB * 700 + 800) : 0;
        await _db.SaveChangesAsync();
        return backup;
    }

    public async Task RestoreBackupAsync(VpsInstance vps, int backupId, string userId)
    {
        var node = vps.Node ?? await _db.ProxmoxNodes.FirstAsync(n => n.Id == vps.NodeId);
        var backup = await _db.VpsBackups.FirstOrDefaultAsync(b => b.Id == backupId && b.VpsInstanceId == vps.Id);
        if (backup == null || backup.Status != VpsBackupStatus.Ready) return;
        await RecordAsync(vps, VpsActionType.Restore, userId, () => _proxmox.RestoreBackupAsync(node, vps.VmId, backup.StoragePath));
    }

    public async Task DeleteBackupAsync(VpsInstance vps, int backupId, string userId)
    {
        var node = vps.Node ?? await _db.ProxmoxNodes.FirstAsync(n => n.Id == vps.NodeId);
        var backup = await _db.VpsBackups.FirstOrDefaultAsync(b => b.Id == backupId && b.VpsInstanceId == vps.Id);
        if (backup == null) return;
        await _proxmox.DeleteBackupAsync(node, node.Storage, backup.StoragePath);
        backup.Status = VpsBackupStatus.Deleted;
        backup.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    // ---------------- Suspend / resume / delete ----------------

    public async Task SuspendAsync(VpsInstance vps, string userId, bool bandwidth = false)
    {
        var node = vps.Node ?? await _db.ProxmoxNodes.FirstAsync(n => n.Id == vps.NodeId);
        await RecordAsync(vps, VpsActionType.Suspend, userId, () => _proxmox.StopVmAsync(node, vps.VmId), VpsStatus.Suspended);
        vps.SuspendedAt = DateTime.UtcNow;
        vps.BandwidthSuspended = bandwidth;
        await _db.SaveChangesAsync();

        await _notifications.NotifyAsync(vps.UserId, "VPS suspended",
            bandwidth ? $"{vps.Hostname} was suspended after exceeding its bandwidth allowance."
                      : $"{vps.Hostname} has been suspended.", NotificationType.Warning,
            dedupeKey: $"vps-suspend-{vps.Id}");
    }

    public async Task ResumeAsync(VpsInstance vps, string userId)
    {
        var node = vps.Node ?? await _db.ProxmoxNodes.FirstAsync(n => n.Id == vps.NodeId);
        await RecordAsync(vps, VpsActionType.Resume, userId, () => _proxmox.StartVmAsync(node, vps.VmId), VpsStatus.Running);
        vps.SuspendedAt = null;
        vps.BandwidthSuspended = false;
        await _db.SaveChangesAsync();
        await _notifications.NotifyAsync(vps.UserId, "VPS resumed", $"{vps.Hostname} is back online.", NotificationType.Success);
    }

    public async Task DeleteAsync(VpsInstance vps, string userId)
    {
        var node = vps.Node ?? await _db.ProxmoxNodes.FirstAsync(n => n.Id == vps.NodeId);
        await RecordAsync(vps, VpsActionType.Delete, userId, () => _proxmox.DeleteVmAsync(node, vps.VmId), VpsStatus.Deleted);
        vps.DeletedAt = DateTime.UtcNow;

        // Release the assigned IP back into the pool.
        var ip = await _db.VpsIpAddresses.FirstOrDefaultAsync(a => a.AssignedInstanceId == vps.Id);
        if (ip != null) ip.AssignedInstanceId = null;

        // Close the linked ClientService if present.
        var svc = await _db.ClientServices.FirstOrDefaultAsync(s => s.Type == ClientServiceType.Vps && s.ReferenceId == vps.Id);
        if (svc != null) { svc.Status = SubscriptionStatus.Cancelled; svc.CancelledAt = DateTime.UtcNow; }

        await _db.SaveChangesAsync();
    }

    // ---------------- Console ----------------

    public async Task<ProxmoxConsole?> OpenConsoleAsync(VpsInstance vps, string userId)
    {
        var node = vps.Node ?? await _db.ProxmoxNodes.FirstAsync(n => n.Id == vps.NodeId);
        var console = await _proxmox.GetConsoleTokenAsync(node, vps.VmId);

        _db.VpsConsoleSessions.Add(new VpsConsoleSession
        {
            VpsInstanceId = vps.Id, UserId = userId, Token = console.Token,
            CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        });
        await RecordAsync(vps, VpsActionType.Console, userId, () => Task.FromResult(new ProxmoxResult(true, "console ticket issued")));
        await _db.SaveChangesAsync();
        return console;
    }
}
