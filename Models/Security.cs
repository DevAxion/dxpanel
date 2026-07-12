using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

// ---------------- Enums ----------------
public enum WafMode { Detection, Prevention }
public enum WafIpAction { Block, Whitelist }
public enum ScanStatus { Clean, Infected, Error }
public enum ScanAction { None, Quarantined, Deleted }
public enum MalwareSeverity { Low, Medium, High, Critical }
public enum MalwareStatus { Detected, Cleaned, FalsePositive, Ignored }
public enum LoginAttemptType { Panel, Ftp, Smtp, Ssh }
public enum DmarcPolicy { None, Quarantine, Reject }
public enum IpRuleKind { WhitelistIp, BlacklistIp, BlockCountry }

// ---------------- 1. ModSecurity (WAF) ----------------
public class WafConfig
{
    public int Id { get; set; }
    public int DomainId { get; set; }
    public Domain? Domain { get; set; }
    public bool Enabled { get; set; }
    public WafMode Mode { get; set; } = WafMode.Detection;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class WafCustomRule
{
    public int Id { get; set; }
    public int DomainId { get; set; }
    /// <summary>ModSecurity SecRule id (900000+ range for custom rules).</summary>
    public long RuleNumber { get; set; }
    [Required] public string RuleText { get; set; } = string.Empty;
    [StringLength(300)] public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class WafIpRule
{
    public int Id { get; set; }
    public int? DomainId { get; set; }
    [Required, StringLength(64)] public string IP { get; set; } = string.Empty;
    public WafIpAction Action { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ModSecurityAlert
{
    public int Id { get; set; }
    public int DomainId { get; set; }
    public Domain? Domain { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    [StringLength(64)] public string IP { get; set; } = string.Empty;
    [StringLength(10)] public string Method { get; set; } = "GET";
    [StringLength(500)] public string URI { get; set; } = string.Empty;
    [StringLength(20)] public string RuleId { get; set; } = string.Empty;
    [StringLength(300)] public string RuleMessage { get; set; } = string.Empty;
    [StringLength(20)] public string Action { get; set; } = "blocked";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ---------------- 2. ClamAV antivirus ----------------
public class ScanResult
{
    public int Id { get; set; }
    [Required] public string UserId { get; set; } = string.Empty;
    [StringLength(600)] public string Path { get; set; } = string.Empty;
    public ScanStatus Status { get; set; } = ScanStatus.Clean;
    [StringLength(200)] public string? ThreatName { get; set; }
    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;
    public ScanAction Action { get; set; } = ScanAction.None;
}

public class QuarantinedFile
{
    public int Id { get; set; }
    [Required] public string UserId { get; set; } = string.Empty;
    [StringLength(600)] public string OriginalPath { get; set; } = string.Empty;
    [StringLength(600)] public string QuarantinePath { get; set; } = string.Empty;
    [StringLength(200)] public string? ThreatName { get; set; }
    public DateTime QuarantinedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RestoredAt { get; set; }
    public bool IsDeleted { get; set; }
}

// ---------------- 3. DKIM / SPF / DMARC ----------------
public class EmailSecurity
{
    public int Id { get; set; }
    public int DomainId { get; set; }
    public Domain? Domain { get; set; }

    public bool DkimEnabled { get; set; }
    [StringLength(60)] public string DkimSelector { get; set; } = "default";
    public string? DkimPublicKey { get; set; }
    public string? DkimPrivateKey { get; set; }

    [StringLength(500)] public string? SpfRecord { get; set; }

    public DmarcPolicy DmarcPolicy { get; set; } = DmarcPolicy.None;
    public int DmarcPercentage { get; set; } = 100;
    [StringLength(200)] public string? DmarcEmail { get; set; }

    public DateTime? LastCheckedAt { get; set; }
    public bool DkimValid { get; set; }
    public bool SpfValid { get; set; }
    public bool DmarcValid { get; set; }
}

// ---------------- 4. Malware scanner ----------------
public class MalwareScanResult
{
    public int Id { get; set; }
    [Required] public string UserId { get; set; } = string.Empty;
    [StringLength(600)] public string FilePath { get; set; } = string.Empty;
    [StringLength(80)] public string ThreatType { get; set; } = string.Empty;
    public MalwareSeverity Severity { get; set; } = MalwareSeverity.Low;
    [StringLength(1000)] public string? Details { get; set; }
    public MalwareStatus Status { get; set; } = MalwareStatus.Detected;
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
}

// ---------------- 5. Brute force protection ----------------
public class LoginAttempt
{
    public int Id { get; set; }
    [StringLength(64)] public string IP { get; set; } = string.Empty;
    [StringLength(256)] public string? Username { get; set; }
    public LoginAttemptType Type { get; set; } = LoginAttemptType.Panel;
    public bool Success { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    [StringLength(400)] public string? UserAgent { get; set; }
    [StringLength(60)] public string? Country { get; set; }
}

public class BlockedIP
{
    public int Id { get; set; }
    [Required, StringLength(64)] public string IP { get; set; } = string.Empty;
    [StringLength(300)] public string Reason { get; set; } = string.Empty;
    public DateTime BlockedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public bool IsManual { get; set; }
    public DateTime? UnblockedAt { get; set; }
    [StringLength(60)] public string? Country { get; set; }

    public bool IsActive => UnblockedAt == null && (ExpiresAt == null || ExpiresAt > DateTime.UtcNow);
}

// ---------------- 6. IP manager ----------------
public class IpAccessRule
{
    public int Id { get; set; }
    /// <summary>An IP, CIDR range, or ISO country code (for BlockCountry).</summary>
    [Required, StringLength(80)] public string Value { get; set; } = string.Empty;
    public IpRuleKind Kind { get; set; }
    [StringLength(300)] public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ---------------- Global security settings (single row Id = 1) ----------------
public class SecuritySettings
{
    public int Id { get; set; } = 1;

    // Brute force
    public int BruteForceMaxAttempts { get; set; } = 5;
    public int BruteForceBlockMinutes { get; set; } = 30;
    public bool ProtectPanel { get; set; } = true;
    public bool ProtectFtp { get; set; } = true;
    public bool ProtectSmtp { get; set; } = true;

    // WAF / OWASP CRS
    [StringLength(20)] public string CrsVersion { get; set; } = "4.3.0";

    // Antivirus
    public bool AvScheduleEnabled { get; set; } = true;
    [StringLength(20)] public string AvSchedule { get; set; } = "daily"; // daily/weekly
    public bool AvScanOnUpload { get; set; }
    public DateTime ClamAvDefinitionsDate { get; set; } = DateTime.UtcNow.AddDays(-1);

    // Malware scanner
    public bool MalwareScheduleEnabled { get; set; } = true;
    [StringLength(20)] public string MalwareSchedule { get; set; } = "weekly";
    public bool AlertOnCritical { get; set; } = true;

    // Rate limiting
    public int RateLimitPerMinute { get; set; } = 120;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
