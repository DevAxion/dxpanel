using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

public enum SslCertType
{
    LetsEncrypt,
    SelfSigned,
    Custom
}

public enum SslCertStatus
{
    Active,
    Expired,
    Pending,
    Revoked
}

public class SslCertificate
{
    public int Id { get; set; }

    [Required]
    public int DomainId { get; set; }
    public Domain? Domain { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public SslCertType Type { get; set; } = SslCertType.LetsEncrypt;

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(90);

    public SslCertStatus Status { get; set; } = SslCertStatus.Active;

    [StringLength(500)]
    public string? CertificatePath { get; set; }

    [StringLength(500)]
    public string? KeyPath { get; set; }

    public int DaysUntilExpiry => (int)(ExpiresAt.Date - DateTime.UtcNow.Date).TotalDays;
}
