using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

// ============================================================
// Phase 16 — Node.js / Python / Ruby / Go app hosting
// ============================================================

public enum AppRuntimeType
{
    NodeJs,
    Python,
    Ruby,
    Go
}

public enum HostedAppStatus
{
    Stopped,
    Starting,
    Running,
    Error
}

public enum AppLogType
{
    Out,
    Error
}

public enum AppDeployType
{
    Manual,
    Git,
    Upload
}

public enum AppDeployStatus
{
    Pending,
    Running,
    Success,
    Failed
}

// ---------------- Installed runtime ----------------

public class AppRuntime
{
    public int Id { get; set; }

    [Required, StringLength(80)] public string Name { get; set; } = string.Empty;
    public AppRuntimeType Type { get; set; }
    [Required, StringLength(40)] public string Version { get; set; } = string.Empty;
    [StringLength(300)] public string BinaryPath { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    /// <summary>The platform default version for this runtime type.</summary>
    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string Icon => Type switch
    {
        AppRuntimeType.NodeJs => "bi-node-plus",
        AppRuntimeType.Python => "bi-filetype-py",
        AppRuntimeType.Ruby => "bi-gem",
        AppRuntimeType.Go => "bi-google",
        _ => "bi-box"
    };
}

// ---------------- Hosted application ----------------

public class HostedApp
{
    public int Id { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public int? DomainId { get; set; }
    public Domain? Domain { get; set; }

    [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
    public AppRuntimeType Type { get; set; }

    public int? RuntimeId { get; set; }
    public AppRuntime? Runtime { get; set; }

    [StringLength(400)] public string AppPath { get; set; } = string.Empty;
    [StringLength(200)] public string EntryPoint { get; set; } = string.Empty;
    [StringLength(400)] public string StartCommand { get; set; } = string.Empty;

    public int Port { get; set; }
    public int ProcessCount { get; set; } = 1;

    public HostedAppStatus Status { get; set; } = HostedAppStatus.Stopped;

    /// <summary>PM2 process id (Node) or the OS pid group for other runtimes.</summary>
    public int? Pm2Id { get; set; }
    public int? Pid { get; set; }
    public long Uptime { get; set; }
    public int RestartCount { get; set; }

    public double MemoryMB { get; set; }
    public double CpuPercent { get; set; }

    public bool AutoRestart { get; set; } = true;
    public bool ClusterMode { get; set; }
    public bool WatchMode { get; set; }
    public int MaxMemoryRestartMB { get; set; } = 256;

    // Python virtualenv
    public bool VirtualenvCreated { get; set; }
    [StringLength(40)] public string? PythonVersion { get; set; }

    // Health
    public DateTime? LastHealthCheckAt { get; set; }
    public bool Healthy { get; set; } = true;
    /// <summary>Auto-restarts performed in the current rolling hour (capped at 3).</summary>
    public int AutoRestartsThisHour { get; set; }
    public DateTime AutoRestartWindowStart { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }

    public ICollection<HostedAppEnv> EnvVars { get; set; } = new List<HostedAppEnv>();

    public string TypeIcon => Type switch
    {
        AppRuntimeType.NodeJs => "bi-node-plus",
        AppRuntimeType.Python => "bi-filetype-py",
        AppRuntimeType.Ruby => "bi-gem",
        AppRuntimeType.Go => "bi-google",
        _ => "bi-box"
    };

    public bool IsNode => Type == AppRuntimeType.NodeJs;
    public bool IsPython => Type == AppRuntimeType.Python;
}

// ---------------- Logs ----------------

public class HostedAppLog
{
    public int Id { get; set; }

    public int HostedAppId { get; set; }
    public HostedApp? HostedApp { get; set; }

    public AppLogType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

// ---------------- Metrics ----------------

public class HostedAppMetric
{
    public int Id { get; set; }

    public int HostedAppId { get; set; }
    public HostedApp? HostedApp { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public double CpuPercent { get; set; }
    public double MemoryMB { get; set; }
    public double RequestsPerSec { get; set; }
    public double ResponseTimeMs { get; set; }
}

// ---------------- Environment variables ----------------

public class HostedAppEnv
{
    public int Id { get; set; }

    public int HostedAppId { get; set; }
    public HostedApp? HostedApp { get; set; }

    [Required, StringLength(120)] public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsSecret { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string Masked => IsSecret && Value.Length > 0
        ? new string('•', Math.Min(12, Value.Length)) : Value;
}

// ---------------- Deployments ----------------

public class HostedAppDeploy
{
    public int Id { get; set; }

    public int HostedAppId { get; set; }
    public HostedApp? HostedApp { get; set; }

    public AppDeployType Type { get; set; }
    public AppDeployStatus Status { get; set; } = AppDeployStatus.Pending;

    [StringLength(60)] public string? CommitHash { get; set; }
    public string? Output { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public double? DurationSeconds => CompletedAt.HasValue ? (CompletedAt.Value - CreatedAt).TotalSeconds : null;
}

// ---------------- Health incidents ----------------

public class HostedAppHealthIncident
{
    public int Id { get; set; }

    public int HostedAppId { get; set; }
    public HostedApp? HostedApp { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
    [StringLength(300)] public string? Reason { get; set; }

    public double DurationSeconds => ((EndedAt ?? DateTime.UtcNow) - StartedAt).TotalSeconds;
    public bool Ongoing => EndedAt == null;
}
