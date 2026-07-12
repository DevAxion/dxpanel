using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

// ---------------- Server node ----------------

public enum NodeType
{
    Primary,
    Secondary,
    Storage,
    Mail,
    Dns
}

public enum NodeStatus
{
    Online,
    Offline,
    Maintenance
}

/// <summary>A physical/virtual server the panel orchestrates over SSH.</summary>
public class ServerNode
{
    public int Id { get; set; }

    [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(200)] public string Hostname { get; set; } = string.Empty;
    [Required, StringLength(60)] public string IpAddress { get; set; } = string.Empty;

    public int SshPort { get; set; } = 22;
    [StringLength(60)] public string SshUsername { get; set; } = "root";
    [StringLength(400)] public string? SshKeyPath { get; set; }

    /// <summary>Stored only when key auth is not used; treat as a secret.</summary>
    public string? SshPassword { get; set; }

    public NodeType Type { get; set; } = NodeType.Primary;
    public NodeStatus Status { get; set; } = NodeStatus.Offline;

    public int CpuCores { get; set; }
    public int RamGB { get; set; }
    public int DiskGB { get; set; }

    [StringLength(80)] public string Os { get; set; } = "Ubuntu 24.04 LTS";
    [StringLength(80)] public string Location { get; set; } = "Frankfurt, DE";

    /// <summary>Relative weight for load balancing (higher = preferred). 0 disables new placement.</summary>
    public int Weight { get; set; } = 100;

    public bool IsActive { get; set; } = true;
    public DateTime? LastPingAt { get; set; }
    /// <summary>Round-trip latency to the node's SSH port, milliseconds.</summary>
    public int? LatencyMs { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Alert thresholds (percent), configurable per node.
    public int CpuThreshold { get; set; } = 90;
    public int RamThreshold { get; set; } = 85;
    public int DiskThreshold { get; set; } = 90;

    public ICollection<ServerService> Services { get; set; } = new List<ServerService>();

    public bool UsesKeyAuth => !string.IsNullOrWhiteSpace(SshKeyPath);
}

// ---------------- Service on a node ----------------

public enum ServerServiceType
{
    Nginx,
    MySQL,
    PHP,
    FTP,
    Email,
    DNS,
    Backup
}

public enum ServerServiceStatus
{
    Running,
    Stopped,
    Error,
    NotInstalled
}

public class ServerService
{
    public int Id { get; set; }

    public int NodeId { get; set; }
    public ServerNode? Node { get; set; }

    public ServerServiceType ServiceType { get; set; }
    public ServerServiceStatus Status { get; set; } = ServerServiceStatus.Running;

    [StringLength(40)] public string? Version { get; set; }
    public int? Port { get; set; }

    public DateTime LastCheckedAt { get; set; } = DateTime.UtcNow;

    /// <summary>The systemd unit name used to start/stop/restart the service.</summary>
    public string UnitName => ServiceType switch
    {
        ServerServiceType.Nginx => "nginx",
        ServerServiceType.MySQL => "mysql",
        ServerServiceType.PHP => "php8.3-fpm",
        ServerServiceType.FTP => "vsftpd",
        ServerServiceType.Email => "postfix",
        ServerServiceType.DNS => "named",
        _ => "srx-backup"
    };
}

// ---------------- Metrics ----------------

public class ServerMetric
{
    public int Id { get; set; }

    public int NodeId { get; set; }
    public ServerNode? Node { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public double CpuPercent { get; set; }
    public double RamPercent { get; set; }
    public double DiskPercent { get; set; }

    public double NetworkInMbps { get; set; }
    public double NetworkOutMbps { get; set; }

    public double LoadAverage1 { get; set; }
    public double LoadAverage5 { get; set; }
    public double LoadAverage15 { get; set; }

    public int ActiveConnections { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ---------------- Placement (domain/db/user → node) ----------------

public class DomainNode
{
    public int Id { get; set; }

    public int DomainId { get; set; }
    public Domain? Domain { get; set; }

    public int NodeId { get; set; }
    public ServerNode? Node { get; set; }

    public bool IsPrimary { get; set; } = true;

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public DateTime? MigratedAt { get; set; }
}

public class DatabaseNode
{
    public int Id { get; set; }

    public int DatabaseId { get; set; }
    public Database? Database { get; set; }

    public int NodeId { get; set; }
    public ServerNode? Node { get; set; }

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}

public class UserNode
{
    public int Id { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public int NodeId { get; set; }
    public ServerNode? Node { get; set; }

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}

// ---------------- Alerts ----------------

public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}

public enum NodeAlertType
{
    CpuHigh,
    RamHigh,
    DiskHigh,
    Unreachable,
    ServiceDown,
    Recovered
}

/// <summary>Fleet-wide load balancer configuration (single row, Id = 1).</summary>
public class LoadBalancerSettings
{
    public int Id { get; set; } = 1;

    /// <summary>When on, new signups are placed on the least-loaded accepting node.</summary>
    public bool AutoBalance { get; set; }

    /// <summary>A node above this CPU% stops accepting new placement.</summary>
    public int CpuThreshold { get; set; } = 80;

    /// <summary>Route new users to a node in their geographic region when possible.</summary>
    public bool GeoRouting { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class NodeAlert
{
    public int Id { get; set; }

    public int NodeId { get; set; }
    public ServerNode? Node { get; set; }

    public NodeAlertType Type { get; set; }
    public AlertSeverity Severity { get; set; }

    [Required, StringLength(400)] public string Message { get; set; } = string.Empty;

    public bool IsAcknowledged { get; set; }
    [StringLength(120)] public string? AcknowledgedBy { get; set; }

    /// <summary>Set once the escalation email/SMS has been sent so it is not resent.</summary>
    public bool Escalated { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AcknowledgedAt { get; set; }

    /// <summary>Dedupe key so a sustained condition does not spam identical alerts.</summary>
    [StringLength(120)] public string? DedupeKey { get; set; }
}
