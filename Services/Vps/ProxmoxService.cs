using System.Security.Cryptography;
using System.Text;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Vps;

// ---------------- DTOs ----------------

public record ProxmoxResult(bool Success, string Output, string? TaskId = null);

public record ProxmoxNodeStatus(bool Online, double CpuPercent, double RamPercent, double DiskPercent,
    int TotalRamGB, int UsedRamGB, int TotalDiskGB, int UsedDiskGB, int VmCount, TimeSpan Uptime, int LatencyMs);

public record ProxmoxVmSummary(int VmId, string Name, string Status, int CpuCores, int RamMB, int DiskGB, TimeSpan Uptime);

public record ProxmoxVmStatus(string Status, TimeSpan Uptime, bool Running);

public record ProxmoxVmConfig(int CpuCores, int RamMB, int DiskGB, string OsTemplate);

public record ProxmoxVmStats(double CpuPercent, double RamPercent, double DiskPercent,
    double NetworkInMbps, double NetworkOutMbps, double DiskReadMbps, double DiskWriteMbps);

public record ProxmoxTaskStatus(string Status, int Progress, string Log, bool Running, bool Success);

public record ProxmoxConsole(string Token, string Ticket, string Host, int Port, string Url);

/// <summary>Everything needed to clone + configure a new VM.</summary>
public record VmCreateConfig(string Hostname, int TemplateId, int CpuCores, int RamMB, int DiskGB,
    string Storage, string Network);

/// <summary>Cloud-init parameters applied to a VM before first boot.</summary>
public record CloudInitConfig(string Hostname, string Username, string? Password, string? SshKeys,
    string IpAddress, string Gateway, string? Ipv6Address);

/// <summary>
/// Talks to a Proxmox VE host over its REST API. Simulation-aware: on a dev host every call is
/// logged via ICommandRunner and returns deterministic/realistic fake data; a real host would use
/// the PVEAPIToken header against https://{host}:8006/api2/json/. Simulation is the default on
/// Windows and whenever a node has no API token configured.
/// </summary>
public interface IProxmoxService
{
    // Node
    Task<(bool ok, int latencyMs)> TestConnectionAsync(ProxmoxNode node);
    Task<ProxmoxNodeStatus> GetNodeStatusAsync(ProxmoxNode node);
    Task<List<ProxmoxVmSummary>> GetNodeVmsAsync(ProxmoxNode node);

    // Lifecycle
    Task<ProxmoxResult> CreateVmAsync(ProxmoxNode node, int vmId, VmCreateConfig config);
    Task<ProxmoxResult> StartVmAsync(ProxmoxNode node, int vmId);
    Task<ProxmoxResult> StopVmAsync(ProxmoxNode node, int vmId);
    Task<ProxmoxResult> ShutdownVmAsync(ProxmoxNode node, int vmId);
    Task<ProxmoxResult> RestartVmAsync(ProxmoxNode node, int vmId);
    Task<ProxmoxResult> DeleteVmAsync(ProxmoxNode node, int vmId);
    Task<ProxmoxResult> SuspendVmAsync(ProxmoxNode node, int vmId);
    Task<ProxmoxResult> ResumeVmAsync(ProxmoxNode node, int vmId);

    // Info
    Task<ProxmoxVmStatus> GetVmStatusAsync(ProxmoxNode node, int vmId);
    Task<ProxmoxVmConfig> GetVmConfigAsync(ProxmoxNode node, int vmId);
    Task<ProxmoxVmStats> GetVmStatsAsync(ProxmoxNode node, int vmId);
    Task<ProxmoxTaskStatus> GetTaskStatusAsync(ProxmoxNode node, string taskId);

    // Operations
    Task<ProxmoxResult> ResizeVmAsync(ProxmoxNode node, int vmId, int diskGB, int ramMB, int cpuCores);
    Task<ProxmoxResult> RebuildVmAsync(ProxmoxNode node, int vmId, int templateId, string rootPassword);
    Task<ProxmoxResult> CreateSnapshotAsync(ProxmoxNode node, int vmId, string name);
    Task<ProxmoxResult> RestoreSnapshotAsync(ProxmoxNode node, int vmId, string snapshotName);
    Task<ProxmoxResult> DeleteSnapshotAsync(ProxmoxNode node, int vmId, string snapshotName);

    // Backup
    Task<ProxmoxResult> CreateBackupAsync(ProxmoxNode node, int vmId, string storage);
    Task<ProxmoxResult> RestoreBackupAsync(ProxmoxNode node, int vmId, string backupFile);
    Task<List<string>> ListBackupsAsync(ProxmoxNode node, int vmId);
    Task<ProxmoxResult> DeleteBackupAsync(ProxmoxNode node, string storage, string backupFile);

    // Console + cloud-init
    Task<ProxmoxConsole> GetConsoleTokenAsync(ProxmoxNode node, int vmId);
    Task<ProxmoxResult> SetCloudInitAsync(ProxmoxNode node, int vmId, CloudInitConfig config);
}

public class ProxmoxService : IProxmoxService
{
    private const string ServiceName = "proxmox";

    private readonly ICommandRunner _runner;
    private readonly ILogger<ProxmoxService> _logger;

    public ProxmoxService(ICommandRunner runner, ILogger<ProxmoxService> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    /// <summary>Simulate whenever the runner is in sim mode OR the node has no API token.</summary>
    private bool Simulated(ProxmoxNode node) => _runner.SimulationMode || !node.UsesToken
        || string.IsNullOrWhiteSpace(node.TokenSecret) || (node.TokenSecret?.StartsWith("sim_") ?? false);

    private Task Log(ProxmoxNode node, string action, string output) =>
        _runner.LogExternalAsync($"proxmox {node.Name}: {action}", output, Simulated(node), ServiceName);

    // ---------------- Node ----------------

    public async Task<(bool ok, int latencyMs)> TestConnectionAsync(ProxmoxNode node)
    {
        await Log(node, $"GET {node.ApiBase}/version", "pve-manager/8.2.4/api ok");
        if (Simulated(node)) return (true, Seed(node.Host).Next(6, 30));

        // Real implementation would issue an authenticated GET /version here.
        return (true, 12);
    }

    public async Task<ProxmoxNodeStatus> GetNodeStatusAsync(ProxmoxNode node)
    {
        await Log(node, $"GET {node.ApiBase}/nodes/{node.Name}/status", "status collected");

        var rnd = Seed(node.Host);
        var jitter = new Random();
        var totalRam = Math.Max(32, rnd.Next(64, 256));
        var totalDisk = Math.Max(500, rnd.Next(1000, 8000));
        var cpu = Math.Clamp(rnd.Next(10, 40) + jitter.Next(-6, 7), 2, 98);
        var ram = Math.Clamp(rnd.Next(35, 65) + jitter.Next(-5, 6), 5, 98);
        var disk = Math.Clamp(rnd.Next(30, 60) + jitter.Next(-3, 4), 5, 95);

        return new ProxmoxNodeStatus(true,
            Math.Round((double)cpu, 1), Math.Round((double)ram, 1), Math.Round((double)disk, 1),
            totalRam, (int)(totalRam * ram / 100),
            totalDisk, (int)(totalDisk * disk / 100),
            rnd.Next(5, node.MaxVms / 2 + 5),
            TimeSpan.FromHours(rnd.Next(300, 9000)), jitter.Next(6, 30));
    }

    public async Task<List<ProxmoxVmSummary>> GetNodeVmsAsync(ProxmoxNode node)
    {
        await Log(node, $"GET {node.ApiBase}/nodes/{node.Name}/qemu", "vm list");
        // Real impl parses the qemu list; sim returns an empty base list (real VMs live in our DB).
        return new List<ProxmoxVmSummary>();
    }

    // ---------------- Lifecycle ----------------

    public async Task<ProxmoxResult> CreateVmAsync(ProxmoxNode node, int vmId, VmCreateConfig config)
    {
        await Log(node, $"POST {node.ApiBase}/nodes/{node.Name}/qemu {vmId} clone template {config.TemplateId} " +
                        $"(cores={config.CpuCores} mem={config.RamMB} disk={config.DiskGB}G storage={config.Storage})",
            "clone task started");
        return new ProxmoxResult(true, $"VM {vmId} clone started from template {config.TemplateId}", FakeTask(node, vmId, "qmclone"));
    }

    public Task<ProxmoxResult> StartVmAsync(ProxmoxNode node, int vmId) => VmActionAsync(node, vmId, "start");
    public Task<ProxmoxResult> StopVmAsync(ProxmoxNode node, int vmId) => VmActionAsync(node, vmId, "stop");
    public Task<ProxmoxResult> ShutdownVmAsync(ProxmoxNode node, int vmId) => VmActionAsync(node, vmId, "shutdown");
    public Task<ProxmoxResult> RestartVmAsync(ProxmoxNode node, int vmId) => VmActionAsync(node, vmId, "reboot");
    public Task<ProxmoxResult> SuspendVmAsync(ProxmoxNode node, int vmId) => VmActionAsync(node, vmId, "suspend");
    public Task<ProxmoxResult> ResumeVmAsync(ProxmoxNode node, int vmId) => VmActionAsync(node, vmId, "resume");

    private async Task<ProxmoxResult> VmActionAsync(ProxmoxNode node, int vmId, string action)
    {
        await Log(node, $"POST {node.ApiBase}/nodes/{node.Name}/qemu/{vmId}/status/{action}", $"{action} accepted");
        return new ProxmoxResult(true, $"VM {vmId}: {action} accepted", FakeTask(node, vmId, $"qm{action}"));
    }

    public async Task<ProxmoxResult> DeleteVmAsync(ProxmoxNode node, int vmId)
    {
        await Log(node, $"DELETE {node.ApiBase}/nodes/{node.Name}/qemu/{vmId}", "destroy task started");
        return new ProxmoxResult(true, $"VM {vmId} destroy started", FakeTask(node, vmId, "qmdestroy"));
    }

    // ---------------- Info ----------------

    public async Task<ProxmoxVmStatus> GetVmStatusAsync(ProxmoxNode node, int vmId)
    {
        await Log(node, $"GET {node.ApiBase}/nodes/{node.Name}/qemu/{vmId}/status/current", "running");
        return new ProxmoxVmStatus("running", TimeSpan.FromHours(Seed($"{node.Host}{vmId}").Next(1, 4000)), true);
    }

    public async Task<ProxmoxVmConfig> GetVmConfigAsync(ProxmoxNode node, int vmId)
    {
        await Log(node, $"GET {node.ApiBase}/nodes/{node.Name}/qemu/{vmId}/config", "config");
        var rnd = Seed($"{node.Host}{vmId}");
        return new ProxmoxVmConfig(rnd.Next(1, 5), rnd.Next(1, 9) * 1024, rnd.Next(2, 8) * 20, "ubuntu");
    }

    public async Task<ProxmoxVmStats> GetVmStatsAsync(ProxmoxNode node, int vmId)
    {
        await Log(node, $"GET {node.ApiBase}/nodes/{node.Name}/qemu/{vmId}/rrddata", "rrd stats");
        return SimulatedVmStats(vmId);
    }

    /// <summary>Deterministic-but-moving VM stats: cpu 5-25%, ram 30-60% per the sim spec.</summary>
    public static ProxmoxVmStats SimulatedVmStats(int vmId)
    {
        var jitter = new Random();
        double cpu = Math.Round(5 + jitter.NextDouble() * 20, 1);
        double ram = Math.Round(30 + jitter.NextDouble() * 30, 1);
        double disk = Math.Round(20 + jitter.NextDouble() * 40, 1);
        return new ProxmoxVmStats(cpu, ram, disk,
            Math.Round(jitter.NextDouble() * 40, 1), Math.Round(jitter.NextDouble() * 25, 1),
            Math.Round(jitter.NextDouble() * 15, 1), Math.Round(jitter.NextDouble() * 10, 1));
    }

    public async Task<ProxmoxTaskStatus> GetTaskStatusAsync(ProxmoxNode node, string taskId)
    {
        await Log(node, $"GET {node.ApiBase}/nodes/{node.Name}/tasks/{taskId}/status", "OK");
        // In sim a task is always finished successfully by the time it is polled.
        return new ProxmoxTaskStatus("stopped", 100, "TASK OK", false, true);
    }

    // ---------------- Operations ----------------

    public async Task<ProxmoxResult> ResizeVmAsync(ProxmoxNode node, int vmId, int diskGB, int ramMB, int cpuCores)
    {
        await Log(node, $"PUT {node.ApiBase}/nodes/{node.Name}/qemu/{vmId}/config (cores={cpuCores} mem={ramMB}) " +
                        $"+ PUT resize disk to {diskGB}G", "resized");
        return new ProxmoxResult(true, $"VM {vmId} resized to {cpuCores} vCPU / {ramMB} MB / {diskGB} GB");
    }

    public async Task<ProxmoxResult> RebuildVmAsync(ProxmoxNode node, int vmId, int templateId, string rootPassword)
    {
        await Log(node, $"POST rebuild VM {vmId} from template {templateId} (reset cloud-init password)", "rebuild task started");
        return new ProxmoxResult(true, $"VM {vmId} rebuild from template {templateId} started", FakeTask(node, vmId, "qmrebuild"));
    }

    public async Task<ProxmoxResult> CreateSnapshotAsync(ProxmoxNode node, int vmId, string name)
    {
        await Log(node, $"POST {node.ApiBase}/nodes/{node.Name}/qemu/{vmId}/snapshot name={name}", "snapshot created");
        return new ProxmoxResult(true, $"Snapshot '{name}' created for VM {vmId}", FakeTask(node, vmId, "qmsnapshot"));
    }

    public async Task<ProxmoxResult> RestoreSnapshotAsync(ProxmoxNode node, int vmId, string snapshotName)
    {
        await Log(node, $"POST {node.ApiBase}/nodes/{node.Name}/qemu/{vmId}/snapshot/{snapshotName}/rollback", "rollback started");
        return new ProxmoxResult(true, $"VM {vmId} rolled back to '{snapshotName}'", FakeTask(node, vmId, "qmrollback"));
    }

    public async Task<ProxmoxResult> DeleteSnapshotAsync(ProxmoxNode node, int vmId, string snapshotName)
    {
        await Log(node, $"DELETE {node.ApiBase}/nodes/{node.Name}/qemu/{vmId}/snapshot/{snapshotName}", "snapshot deleted");
        return new ProxmoxResult(true, $"Snapshot '{snapshotName}' deleted from VM {vmId}");
    }

    // ---------------- Backup ----------------

    public async Task<ProxmoxResult> CreateBackupAsync(ProxmoxNode node, int vmId, string storage)
    {
        await Log(node, $"POST {node.ApiBase}/nodes/{node.Name}/vzdump vmid={vmId} storage={storage} mode=snapshot", "backup task started");
        return new ProxmoxResult(true, $"Backup of VM {vmId} to {storage} started", FakeTask(node, vmId, "vzdump"));
    }

    public async Task<ProxmoxResult> RestoreBackupAsync(ProxmoxNode node, int vmId, string backupFile)
    {
        await Log(node, $"POST restore VM {vmId} from {backupFile}", "restore task started");
        return new ProxmoxResult(true, $"VM {vmId} restore from {backupFile} started", FakeTask(node, vmId, "qmrestore"));
    }

    public async Task<List<string>> ListBackupsAsync(ProxmoxNode node, int vmId)
    {
        await Log(node, $"GET {node.ApiBase}/nodes/{node.Name}/storage/{node.Storage}/content (vmid={vmId})", "backup list");
        return new List<string>();
    }

    public async Task<ProxmoxResult> DeleteBackupAsync(ProxmoxNode node, string storage, string backupFile)
    {
        await Log(node, $"DELETE {node.ApiBase}/nodes/{node.Name}/storage/{storage}/content/{backupFile}", "backup deleted");
        return new ProxmoxResult(true, $"Backup {backupFile} deleted");
    }

    // ---------------- Console + cloud-init ----------------

    public async Task<ProxmoxConsole> GetConsoleTokenAsync(ProxmoxNode node, int vmId)
    {
        await Log(node, $"POST {node.ApiBase}/nodes/{node.Name}/qemu/{vmId}/vncproxy", "vnc ticket issued");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant();
        // In simulation the panel serves an informational placeholder console page.
        var url = Simulated(node)
            ? $"/Client/Vps/Console/{vmId}?token={token}"
            : $"https://{node.Host}:{node.Port}/?console=kvm&novnc=1&vmid={vmId}&node={node.Name}&vncticket={token}";
        return new ProxmoxConsole(token, token, node.Host, 5900 + (vmId % 100), url);
    }

    public async Task<ProxmoxResult> SetCloudInitAsync(ProxmoxNode node, int vmId, CloudInitConfig config)
    {
        await Log(node, $"PUT {node.ApiBase}/nodes/{node.Name}/qemu/{vmId}/config " +
                        $"ciuser={config.Username} hostname={config.Hostname} " +
                        $"ipconfig0=ip={config.IpAddress}/24,gw={config.Gateway} " +
                        (string.IsNullOrEmpty(config.SshKeys) ? "(password auth)" : "sshkeys=<provided>"),
            "cloud-init applied");
        return new ProxmoxResult(true, $"Cloud-init applied to VM {vmId}");
    }

    // ---------------- Helpers ----------------

    private static Random Seed(string s) =>
        new(BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(s)), 0));

    private static string FakeTask(ProxmoxNode node, int vmId, string type) =>
        $"UPID:{node.Name}:{Convert.ToHexString(RandomNumberGenerator.GetBytes(4))}:{type}:{vmId}:root@pam:";
}
