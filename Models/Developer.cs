using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

// ---------------- Cron ----------------

public enum CronJobState
{
    Active,
    Disabled,
    Running,
    Error
}

/// <summary>A user-scheduled command. Commands are sandboxed to the user's home directory.</summary>
public class CronJob
{
    public int Id { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [Required, StringLength(500)] public string Command { get; set; } = string.Empty;
    [Required, StringLength(120)] public string Schedule { get; set; } = "0 3 * * *";
    [StringLength(200)] public string? Description { get; set; }

    public bool IsActive { get; set; } = true;
    /// <summary>True while an execution is in flight — surfaces the "Running" badge.</summary>
    public bool IsRunning { get; set; }

    [StringLength(200)] public string? Email { get; set; }
    public bool EmailOnSuccess { get; set; }
    public bool EmailOnFailure { get; set; } = true;

    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }
    public int? LastExitCode { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public CronJobState State =>
        IsRunning ? CronJobState.Running
        : !IsActive ? CronJobState.Disabled
        : LastExitCode is int c && c != 0 ? CronJobState.Error
        : CronJobState.Active;
}

public class CronJobLog
{
    public int Id { get; set; }

    public int CronJobId { get; set; }
    public CronJob? CronJob { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public int ExitCode { get; set; }
    public string Output { get; set; } = string.Empty;
    /// <summary>Execution duration in milliseconds.</summary>
    public long DurationMs { get; set; }

    /// <summary>True when the run was started from the "Run now" button rather than the scheduler.</summary>
    public bool Manual { get; set; }
}

// ---------------- SSH ----------------

public class SshKey
{
    public int Id { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [Required, StringLength(100)] public string Label { get; set; } = string.Empty;
    [Required] public string PublicKey { get; set; } = string.Empty;
    /// <summary>OpenSSH-style fingerprint, e.g. SHA256:xxxxx.</summary>
    [StringLength(120)] public string Fingerprint { get; set; } = string.Empty;
    /// <summary>Key algorithm, e.g. ssh-ed25519.</summary>
    [StringLength(40)] public string KeyType { get; set; } = string.Empty;

    public DateTime? LastUsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class SshAccess
{
    public int Id { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public bool IsEnabled { get; set; }
    public int Port { get; set; } = 22;
    public bool AllowPasswordAuth { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class SshAccessLog
{
    public int Id { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;

    [StringLength(60)] public string IpAddress { get; set; } = string.Empty;
    [StringLength(120)] public string? KeyFingerprint { get; set; }
    public bool Success { get; set; } = true;
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
}

// ---------------- Git deployment ----------------

public enum GitRepoStatus
{
    Idle,
    Deploying,
    Failed
}

public enum GitTriggerType
{
    Manual,
    Webhook,
    Schedule
}

public enum GitDeployStatus
{
    Pending,
    Running,
    Success,
    Failed
}

public class GitRepository
{
    public int Id { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public int DomainId { get; set; }
    public Domain? Domain { get; set; }

    [Required, StringLength(400)] public string RepoUrl { get; set; } = string.Empty;
    [Required, StringLength(120)] public string Branch { get; set; } = "main";
    [Required, StringLength(400)] public string DeployPath { get; set; } = "/";

    public int? SshKeyId { get; set; }
    public SshKey? SshKey { get; set; }

    [StringLength(80)] public string WebhookSecret { get; set; } = string.Empty;
    public bool AutoDeploy { get; set; } = true;

    /// <summary>Newline-separated shell commands run after every successful pull.</summary>
    public string? PostDeployCommands { get; set; }

    public DateTime? LastDeployAt { get; set; }
    [StringLength(60)] public string? LastCommitHash { get; set; }
    [StringLength(400)] public string? LastCommitMessage { get; set; }

    public GitRepoStatus Status { get; set; } = GitRepoStatus.Idle;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public IEnumerable<string> PostDeployList =>
        (PostDeployCommands ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Short display name, e.g. "octocat/hello-world".</summary>
    public string ShortName
    {
        get
        {
            var s = RepoUrl.TrimEnd('/');
            if (s.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) s = s[..^4];

            // Pass the separators as an array: Split('/', ':', options) would bind to the
            // Split(char, int count, options) overload, with ':' silently becoming a count.
            var parts = s.Split(new[] { '/', ':' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? $"{parts[^2]}/{parts[^1]}" : s;
        }
    }
}

public class GitDeployment
{
    public int Id { get; set; }

    public int RepositoryId { get; set; }
    public GitRepository? Repository { get; set; }

    public GitTriggerType TriggerType { get; set; } = GitTriggerType.Manual;

    [StringLength(60)] public string? CommitHash { get; set; }
    [StringLength(400)] public string? CommitMessage { get; set; }

    public GitDeployStatus Status { get; set; } = GitDeployStatus.Pending;
    public string Output { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public TimeSpan? Duration => CompletedAt is DateTime c ? c - StartedAt : null;
}

// ---------------- Browser terminal ----------------

public class TerminalSession
{
    public int Id { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    /// <summary>JWT id (jti) of the short-lived connection token.</summary>
    [StringLength(80)] public string TokenId { get; set; } = string.Empty;

    [StringLength(60)] public string IpAddress { get; set; } = string.Empty;
    [StringLength(300)] public string? UserAgent { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    public bool IsActive { get; set; } = true;
    /// <summary>Set by an admin to force the socket to close on its next tick.</summary>
    public bool TerminationRequested { get; set; }

    public int CommandCount { get; set; }

    public TimeSpan Duration => (EndedAt ?? DateTime.UtcNow) - StartedAt;
}

// ---------------- PHP configuration ----------------

/// <summary>Per-domain user-editable php.ini overrides, written to the user's .php.ini.</summary>
public class PhpConfig
{
    public int Id { get; set; }

    public int DomainId { get; set; }
    public Domain? Domain { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;

    [StringLength(16)] public string MemoryLimit { get; set; } = "256M";
    [Range(5, 300)] public int MaxExecutionTime { get; set; } = 30;
    [StringLength(16)] public string UploadMaxFilesize { get; set; } = "64M";
    [StringLength(16)] public string PostMaxSize { get; set; } = "64M";
    [Range(100, 10000)] public int MaxInputVars { get; set; } = 1000;
    [StringLength(60)] public string Timezone { get; set; } = "UTC";

    public bool DisplayErrors { get; set; }
    [StringLength(60)] public string ErrorReporting { get; set; } = "E_ALL & ~E_DEPRECATED & ~E_STRICT";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// ---------------- Staging ----------------

public enum StagingStatus
{
    Creating,
    Active,
    Syncing,
    Failed
}

public class StagingSite
{
    public int Id { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;

    public int DomainId { get; set; }
    public Domain? Domain { get; set; }

    [Required, StringLength(300)] public string StagingDomain { get; set; } = string.Empty;
    [StringLength(500)] public string StagingPath { get; set; } = string.Empty;
    [StringLength(64)] public string? DatabaseName { get; set; }
    [StringLength(20)] public string? TablePrefix { get; set; }

    public bool PasswordProtected { get; set; }
    [StringLength(60)] public string? AuthUser { get; set; }
    public string? AuthPasswordHash { get; set; }

    /// <summary>Auto-delete date; null means the staging site never expires.</summary>
    public DateTime? ExpiresAt { get; set; }

    public StagingStatus Status { get; set; } = StagingStatus.Creating;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSyncAt { get; set; }
    /// <summary>Direction of the last sync: "clone", "push" or null.</summary>
    [StringLength(20)] public string? LastSyncDirection { get; set; }
}

// ---------------- Developer settings ----------------

/// <summary>Per-user developer preferences (one row per user).</summary>
public class DeveloperSettings
{
    public int Id { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    /// <summary>Surfaces full stack traces in error pages for this account's sites.</summary>
    public bool DebugMode { get; set; }
    [StringLength(200), EmailAddress] public string? ErrorReportingEmail { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A single outbound webhook delivery attempt, shown in the delivery log.</summary>
public class WebhookDelivery
{
    public int Id { get; set; }

    public int WebhookEndpointId { get; set; }
    public WebhookEndpoint? Endpoint { get; set; }

    [StringLength(60)] public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;

    public int ResponseCode { get; set; }
    [StringLength(500)] public string? ResponseBody { get; set; }
    public long DurationMs { get; set; }
    public bool Success { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
