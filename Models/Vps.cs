using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

// ============================================================
// Phase 12 — VPS provisioning (Proxmox)
// ============================================================

// ---------------- Proxmox node (hypervisor) ----------------

/// <summary>A Proxmox VE host the panel provisions VMs onto over the REST API.</summary>
public class ProxmoxNode
{
    public int Id { get; set; }

    [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(200)] public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 8006;

    [StringLength(80)] public string Username { get; set; } = "root@pam";
    /// <summary>API token id, e.g. "root@pam!srxpanel".</summary>
    [StringLength(120)] public string TokenId { get; set; } = string.Empty;
    /// <summary>API token secret — treat as sensitive.</summary>
    public string? TokenSecret { get; set; }

    public bool VerifySsl { get; set; }
    public bool IsActive { get; set; } = true;

    public int MaxVms { get; set; } = 100;

    /// <summary>Default Proxmox storage id for disks/backups (e.g. "local-lvm").</summary>
    [StringLength(80)] public string Storage { get; set; } = "local-lvm";
    /// <summary>Default bridge for VM NICs (e.g. "vmbr0").</summary>
    [StringLength(80)] public string Network { get; set; } = "vmbr0";

    [StringLength(80)] public string Location { get; set; } = "Frankfurt, DE";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSeenAt { get; set; }

    public ICollection<VpsTemplate> Templates { get; set; } = new List<VpsTemplate>();

    public bool UsesToken => !string.IsNullOrWhiteSpace(TokenId);
    public string ApiBase => $"https://{Host}:{Port}/api2/json";
}

// ---------------- OS template ----------------

public class VpsTemplate
{
    public int Id { get; set; }

    public int NodeId { get; set; }
    public ProxmoxNode? Node { get; set; }

    [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
    /// <summary>OS slug used for icon lookup, e.g. "ubuntu", "debian", "centos", "rocky".</summary>
    [StringLength(40)] public string OsType { get; set; } = "ubuntu";

    /// <summary>Proxmox VMID of the template to clone from.</summary>
    public int TemplateId { get; set; }

    [StringLength(300)] public string? Description { get; set; }

    public int MinDiskGB { get; set; } = 10;
    public int MinRamMB { get; set; } = 512;
    public int MinCpuCores { get; set; } = 1;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string IconClass => OsType.ToLowerInvariant() switch
    {
        "ubuntu" => "bi-ubuntu",
        "debian" => "bi-hdd-network",
        "centos" or "rocky" or "almalinux" => "bi-server",
        "fedora" => "bi-hdd",
        "windows" => "bi-windows",
        _ => "bi-hdd-stack"
    };
}

// ---------------- VPS instance ----------------

public enum VpsStatus
{
    Building,
    Running,
    Stopped,
    Suspended,
    Deleted,
    Error
}

public class VpsInstance
{
    public int Id { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public int PlanId { get; set; }
    public VpsPlan? Plan { get; set; }

    public int NodeId { get; set; }
    public ProxmoxNode? Node { get; set; }

    /// <summary>Proxmox VMID.</summary>
    public int VmId { get; set; }

    [Required, StringLength(120)] public string Hostname { get; set; } = string.Empty;

    [StringLength(60)] public string? IpAddress { get; set; }
    [StringLength(80)] public string? Ipv6Address { get; set; }
    [StringLength(40)] public string? MacAddress { get; set; }
    /// <summary>Reverse DNS (PTR) for the primary IP.</summary>
    [StringLength(200)] public string? ReverseDns { get; set; }

    public VpsStatus Status { get; set; } = VpsStatus.Building;

    [StringLength(60)] public string OsTemplate { get; set; } = "ubuntu";

    public int CpuCores { get; set; }
    public int RamMB { get; set; }
    public int DiskGB { get; set; }
    public int BandwidthGB { get; set; }
    /// <summary>Bandwidth used this billing cycle, in GB.</summary>
    public double BandwidthUsed { get; set; }

    /// <summary>Root password — sensitive; shown once to the client.</summary>
    public string? RootPassword { get; set; }
    public int SshPort { get; set; } = 22;

    [StringLength(400)] public string? Notes { get; set; }
    public bool NotifyBandwidth { get; set; } = true;
    public bool NotifyPower { get; set; } = true;

    /// <summary>Set true when a bandwidth-overage suspension is applied (vs a manual/billing one).</summary>
    public bool BandwidthSuspended { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? SuspendedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    /// <summary>Start of the current bandwidth billing cycle.</summary>
    public DateTime BandwidthCycleStart { get; set; } = DateTime.UtcNow;

    public ICollection<VpsFirewallRule> FirewallRules { get; set; } = new List<VpsFirewallRule>();

    public double BandwidthPercent => BandwidthGB <= 0 ? 0 : Math.Min(100, Math.Round(100.0 * BandwidthUsed / BandwidthGB, 1));
    public bool IsAlive => Status is VpsStatus.Running or VpsStatus.Stopped or VpsStatus.Building;
}

// ---------------- Action / task log ----------------

public enum VpsActionType
{
    Create,
    Start,
    Stop,
    Restart,
    Shutdown,
    Rebuild,
    Resize,
    Suspend,
    Resume,
    Delete,
    Backup,
    Restore,
    Snapshot,
    Console
}

public enum VpsActionStatus
{
    Pending,
    Running,
    Success,
    Failed
}

public class VpsAction
{
    public int Id { get; set; }

    public int VpsInstanceId { get; set; }
    public VpsInstance? VpsInstance { get; set; }

    [StringLength(80)] public string UserId { get; set; } = string.Empty;

    public VpsActionType Action { get; set; }
    public VpsActionStatus Status { get; set; } = VpsActionStatus.Pending;

    public string? Output { get; set; }
    /// <summary>Proxmox task id (UPID) when the action maps to a Proxmox task.</summary>
    [StringLength(200)] public string? TaskId { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

// ---------------- Backups & snapshots ----------------

public enum VpsBackupStatus
{
    Creating,
    Ready,
    Failed,
    Deleted
}

public class VpsBackup
{
    public int Id { get; set; }

    public int VpsInstanceId { get; set; }
    public VpsInstance? VpsInstance { get; set; }

    [StringLength(80)] public string UserId { get; set; } = string.Empty;

    /// <summary>Backup size in MB.</summary>
    public long SizeMB { get; set; }
    [StringLength(400)] public string StoragePath { get; set; } = string.Empty;

    public VpsBackupStatus Status { get; set; } = VpsBackupStatus.Creating;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }
}

public enum VpsSnapshotStatus
{
    Creating,
    Ready,
    Failed,
    Deleted
}

public class VpsSnapshot
{
    public int Id { get; set; }

    public int VpsInstanceId { get; set; }
    public VpsInstance? VpsInstance { get; set; }

    [Required, StringLength(60)] public string Name { get; set; } = string.Empty;
    [StringLength(200)] public string? Description { get; set; }

    public VpsSnapshotStatus Status { get; set; } = VpsSnapshotStatus.Creating;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }
}

// ---------------- Console session ----------------

public class VpsConsoleSession
{
    public int Id { get; set; }

    public int VpsInstanceId { get; set; }
    public VpsInstance? VpsInstance { get; set; }

    [StringLength(80)] public string UserId { get; set; } = string.Empty;

    [Required, StringLength(200)] public string Token { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(10);

    public bool IsValid => DateTime.UtcNow < ExpiresAt;
}

// ---------------- Metrics ----------------

public class VpsMetric
{
    public int Id { get; set; }

    public int VpsInstanceId { get; set; }
    public VpsInstance? VpsInstance { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public double CpuPercent { get; set; }
    public double RamPercent { get; set; }
    public double DiskPercent { get; set; }

    public double NetworkInMbps { get; set; }
    public double NetworkOutMbps { get; set; }
    public double DiskReadMbps { get; set; }
    public double DiskWriteMbps { get; set; }
}

// ---------------- IP pool ----------------

/// <summary>An address in a node's IP pool, optionally assigned to an instance.</summary>
public class VpsIpAddress
{
    public int Id { get; set; }

    public int NodeId { get; set; }
    public ProxmoxNode? Node { get; set; }

    [Required, StringLength(60)] public string Address { get; set; } = string.Empty;
    public bool IsIpv6 { get; set; }

    [StringLength(60)] public string? Gateway { get; set; }
    /// <summary>CIDR prefix length, e.g. 24 for IPv4 or 64 for IPv6.</summary>
    public int Prefix { get; set; } = 24;

    /// <summary>Instance this address is assigned to, if any.</summary>
    public int? AssignedInstanceId { get; set; }
    /// <summary>Reserved addresses (gateway, panel, etc.) are never auto-assigned.</summary>
    public bool IsReserved { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsAvailable => AssignedInstanceId == null && !IsReserved;
}

// ---------------- Firewall ----------------

public enum VpsFirewallAction
{
    Allow,
    Deny
}

public class VpsFirewallRule
{
    public int Id { get; set; }

    public int VpsInstanceId { get; set; }
    public VpsInstance? VpsInstance { get; set; }

    public VpsFirewallAction Action { get; set; } = VpsFirewallAction.Allow;
    /// <summary>tcp / udp.</summary>
    [StringLength(10)] public string Protocol { get; set; } = "tcp";
    public int Port { get; set; }
    /// <summary>Source CIDR; empty/"any" = all sources.</summary>
    [StringLength(60)] public string Source { get; set; } = "any";

    [StringLength(120)] public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
