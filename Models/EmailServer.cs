using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

// ============================================================
// Phase 15 — Email server management + queue + blacklist
// ============================================================

// ---------------- Mail queue ----------------

public enum EmailQueueStatus
{
    Queued,
    Sending,
    Sent,
    Failed,
    Deferred
}

public class EmailQueue
{
    public int Id { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public int? DomainId { get; set; }
    public Domain? Domain { get; set; }

    [Required, StringLength(255)] public string FromAddress { get; set; } = string.Empty;
    [Required, StringLength(255)] public string ToAddress { get; set; } = string.Empty;
    [StringLength(400)] public string Subject { get; set; } = string.Empty;
    public string? Body { get; set; }

    public EmailQueueStatus Status { get; set; } = EmailQueueStatus.Queued;
    public int Attempts { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    [StringLength(500)] public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
}

/// <summary>Per-domain, per-day rollup of mail outcomes for charts and delivery rate.</summary>
public class EmailQueueStats
{
    public int Id { get; set; }

    public int DomainId { get; set; }
    public Domain? Domain { get; set; }

    public DateTime Date { get; set; }

    public int TotalSent { get; set; }
    public int TotalFailed { get; set; }
    public int TotalDeferred { get; set; }
    public int TotalBounced { get; set; }
    public int TotalSpam { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int Total => TotalSent + TotalFailed + TotalDeferred + TotalBounced;
    public double DeliveryRate => Total <= 0 ? 100 : Math.Round(100.0 * TotalSent / Total, 1);
}

// ---------------- Blacklists ----------------

public enum BlacklistValueType
{
    IP,
    Domain
}

public enum BlacklistCheckType
{
    IP,
    Domain,
    Email
}

public enum BlacklistCheckStatus
{
    Clean,
    Listed
}

/// <summary>A single (value × blacklist) listing state, tracked over time.</summary>
public class BlacklistEntry
{
    public int Id { get; set; }

    public BlacklistValueType Type { get; set; }
    [Required, StringLength(255)] public string Value { get; set; } = string.Empty;
    [Required, StringLength(80)] public string BlacklistName { get; set; } = string.Empty;

    public bool IsListed { get; set; }

    public DateTime LastCheckedAt { get; set; } = DateTime.UtcNow;
    public DateTime FirstDetectedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public bool IsResolved { get; set; }

    public int? DomainId { get; set; }
    public Domain? Domain { get; set; }
}

/// <summary>One check run (IP/domain/email against the blacklist set).</summary>
public class BlacklistCheck
{
    public int Id { get; set; }

    public int? DomainId { get; set; }
    public Domain? Domain { get; set; }

    [StringLength(80)] public string UserId { get; set; } = string.Empty;

    public BlacklistCheckType CheckType { get; set; }
    [Required, StringLength(255)] public string Value { get; set; } = string.Empty;

    public BlacklistCheckStatus Status { get; set; } = BlacklistCheckStatus.Clean;
    /// <summary>Comma-separated blacklist names the value was listed on (empty when clean).</summary>
    [StringLength(500)] public string? ListedOn { get; set; }

    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}

// ---------------- Bounces ----------------

public enum BounceType
{
    Hard,
    Soft
}

public class EmailBounce
{
    public int Id { get; set; }

    public int DomainId { get; set; }
    public Domain? Domain { get; set; }

    [Required, StringLength(255)] public string EmailAddress { get; set; } = string.Empty;
    public BounceType BounceType { get; set; }
    [StringLength(400)] public string BounceReason { get; set; } = string.Empty;

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public bool IsBlacklisted { get; set; }
}

// ---------------- Delivery log ----------------

public enum EmailLogStatus
{
    Delivered,
    Deferred,
    Bounced,
    Spam,
    Rejected
}

public class EmailLog
{
    public int Id { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public int? DomainId { get; set; }
    public Domain? Domain { get; set; }

    [StringLength(255)] public string FromAddress { get; set; } = string.Empty;
    [StringLength(255)] public string ToAddress { get; set; } = string.Empty;
    [StringLength(400)] public string Subject { get; set; } = string.Empty;
    [StringLength(200)] public string MessageId { get; set; } = string.Empty;

    public EmailLogStatus Status { get; set; }
    public double SpamScore { get; set; }

    public DateTime? DeliveredAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ---------------- Per-domain mail server config ----------------

public class MailServerConfig
{
    public int Id { get; set; }

    public int DomainId { get; set; }
    public Domain? Domain { get; set; }

    [StringLength(200)] public string SmtpHost { get; set; } = "mail.example.com";
    public int SmtpPort { get; set; } = 587;
    [StringLength(200)] public string ImapHost { get; set; } = "mail.example.com";
    public int ImapPort { get; set; } = 993;
    [StringLength(200)] public string Pop3Host { get; set; } = "mail.example.com";
    public int Pop3Port { get; set; } = 995;

    public bool RequireAuth { get; set; } = true;

    /// <summary>Mailbox quota in MB.</summary>
    public int MaxMailboxSize { get; set; } = 2048;
    /// <summary>Max attachment size in MB.</summary>
    public int MaxAttachmentSize { get; set; } = 25;
    public double SpamThreshold { get; set; } = 5.0;

    /// <summary>Auto-delete spam after N days (0 = keep).</summary>
    public int SpamRetentionDays { get; set; } = 30;
    public bool QuarantineEnabled { get; set; } = true;

    // Queue control
    public bool QueuePaused { get; set; }

    // Blacklist auto-check + alerting
    public bool BlacklistAutoCheck { get; set; }
    /// <summary>daily / weekly.</summary>
    [StringLength(20)] public string BlacklistSchedule { get; set; } = "daily";
    public bool AlertOnBlacklist { get; set; } = true;
    public DateTime? LastBlacklistCheckAt { get; set; }

    // Bounce handling
    public bool AutoBlacklistBounces { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
