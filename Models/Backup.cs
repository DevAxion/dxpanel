using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

public enum BackupType
{
    Full,
    Files,
    Databases,
    Emails
}

public enum BackupStatus
{
    Pending,
    Running,
    Completed,
    Failed
}

public enum BackupFrequency
{
    Manual,
    Daily,
    Weekly,
    Monthly
}

public class Backup
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public BackupType Type { get; set; } = BackupType.Full;
    public BackupStatus Status { get; set; } = BackupStatus.Pending;

    public long SizeBytes { get; set; }

    [StringLength(500)]
    public string? FilePath { get; set; }

    /// <summary>Restore-point label, e.g. "Before WordPress 6.5 update". Null for ordinary backups.</summary>
    [StringLength(200)]
    public string? Label { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    /// <summary>Whether this backup has been copied to off-site (S3/Backblaze) storage.</summary>
    public bool OffsiteStored { get; set; }
    public DateTime? OffsiteUploadedAt { get; set; }
}

public class BackupSchedule
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    public BackupFrequency Frequency { get; set; } = BackupFrequency.Manual;

    // Keep last N backups
    [Range(1, 100)]
    public int Retention { get; set; } = 5;

    // "local" or "s3"
    [StringLength(20)]
    public string Destination { get; set; } = "local";

    [StringLength(500)]
    public string? S3Bucket { get; set; }

    public bool IsEnabled { get; set; }

    public DateTime? LastRunAt { get; set; }
}
