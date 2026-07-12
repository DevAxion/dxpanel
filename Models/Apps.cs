using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

public enum AppCategory
{
    Cms,
    ECommerce,
    Framework,
    Forum,
    Blog,
    Wiki,
    ProjectManagement,
    Support
}

public enum AppInstallStatus
{
    Installing,
    Active,
    UpdateAvailable,
    Error
}

public enum AppJobType
{
    Install,
    Update,
    Uninstall,
    Clone
}

public enum AppJobStatus
{
    Pending,
    Running,
    Success,
    Failed
}

public enum WpAssetType
{
    Plugin,
    Theme
}

/// <summary>A catalogue entry describing an installable web application.</summary>
public class AppDefinition
{
    public int Id { get; set; }

    [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(120)] public string Slug { get; set; } = string.Empty;
    [StringLength(600)] public string Description { get; set; } = string.Empty;

    public AppCategory Category { get; set; }

    [StringLength(40)] public string Version { get; set; } = "1.0.0";
    /// <summary>Bootstrap icon name used as the app tile icon.</summary>
    [StringLength(80)] public string IconPath { get; set; } = "bi-box";

    public int MinDiskMB { get; set; } = 50;
    [StringLength(10)] public string MinPhpVersion { get; set; } = "8.1";

    [StringLength(500)] public string? DownloadUrl { get; set; }
    /// <summary>Optional shell script template run after extraction (simulation-safe).</summary>
    public string? InstallScript { get; set; }

    /// <summary>True when the app needs a MySQL database provisioned.</summary>
    public bool RequiresDatabase { get; set; } = true;

    // Catalogue presentation
    [Range(0, 5)] public double Rating { get; set; } = 4.5;
    public int InstallCount { get; set; }
    /// <summary>Newline-separated feature bullets.</summary>
    public string? Features { get; set; }
    public string? Requirements { get; set; }
    public string? Changelog { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public IEnumerable<string> FeatureList =>
        (Features ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>A concrete installation of an application on a client's domain.</summary>
public class AppInstallation
{
    public int Id { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public int DomainId { get; set; }
    public Domain? Domain { get; set; }

    public int AppDefinitionId { get; set; }
    public AppDefinition? AppDefinition { get; set; }

    [StringLength(40)] public string InstalledVersion { get; set; } = "";
    [StringLength(500)] public string InstallPath { get; set; } = "/";

    [StringLength(64)] public string? DatabaseName { get; set; }
    [StringLength(64)] public string? DatabaseUser { get; set; }
    [StringLength(20)] public string? TablePrefix { get; set; } = "wp_";

    [StringLength(400)] public string SiteUrl { get; set; } = "";
    [StringLength(400)] public string AdminUrl { get; set; } = "";
    [StringLength(200)] public string SiteTitle { get; set; } = "";
    [StringLength(120)] public string? AdminUser { get; set; }
    [StringLength(200)] public string? AdminEmail { get; set; }

    [StringLength(10)] public string PhpVersion { get; set; } = "8.3";
    [StringLength(10)] public string Language { get; set; } = "en";

    public AppInstallStatus Status { get; set; } = AppInstallStatus.Installing;
    /// <summary>Latest available version when an update is pending.</summary>
    [StringLength(40)] public string? AvailableVersion { get; set; }

    public bool AutoUpdate { get; set; } = true;
    /// <summary>True when this installation is a staging clone of another site.</summary>
    public bool IsStaging { get; set; }

    public DateTime InstalledAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUpdatedAt { get; set; }
}

/// <summary>An asynchronous install/update/uninstall/clone job with live progress.</summary>
public class AppInstallJob
{
    public int Id { get; set; }

    public int? InstallationId { get; set; }
    [Required] public string UserId { get; set; } = string.Empty;

    public AppJobType Type { get; set; } = AppJobType.Install;
    public AppJobStatus Status { get; set; } = AppJobStatus.Pending;

    [Range(0, 100)] public int Progress { get; set; }
    [StringLength(200)] public string CurrentStep { get; set; } = "Queued";
    public string Log { get; set; } = "";

    [StringLength(100)] public string AppName { get; set; } = "";

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

/// <summary>A WordPress plugin or theme tracked for an installation.</summary>
public class WpAsset
{
    public int Id { get; set; }

    public int InstallationId { get; set; }
    public AppInstallation? Installation { get; set; }

    public WpAssetType Type { get; set; }

    [Required, StringLength(120)] public string Slug { get; set; } = string.Empty;
    [Required, StringLength(160)] public string Name { get; set; } = string.Empty;
    [StringLength(40)] public string Version { get; set; } = "1.0.0";
    [StringLength(40)] public string? LatestVersion { get; set; }

    public bool IsActive { get; set; }
    public bool UpdateAvailable { get; set; }
    public DateTime InstalledAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Platform-wide auto-update policy (single row, Id = 1).</summary>
public class AppUpdateSettings
{
    public int Id { get; set; } = 1;

    /// <summary>Minor/patch versions are applied automatically.</summary>
    public bool AutoUpdateMinor { get; set; } = true;
    /// <summary>Major versions only notify the client.</summary>
    public bool NotifyMajorOnly { get; set; } = true;

    [StringLength(20)] public string Schedule { get; set; } = "nightly"; // nightly/weekly
    public bool EmailClientOnUpdate { get; set; } = true;
    /// <summary>Number of pre-action restore points retained per installation.</summary>
    [Range(1, 10)] public int KeepRestorePoints { get; set; } = 3;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
