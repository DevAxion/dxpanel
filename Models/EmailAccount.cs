using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

public class EmailAccount
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public int? DomainId { get; set; }
    public Domain? Domain { get; set; }

    [Required]
    [EmailAddress]
    [StringLength(255)]
    public string EmailAddress { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    // 0 = unlimited
    public long QuotaMB { get; set; } = 1024;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Phase 5 email features
    public bool AutoresponderEnabled { get; set; }

    [StringLength(2000)]
    public string? AutoresponderMessage { get; set; }

    // SpamAssassin score threshold (higher = more lenient)
    public double SpamThreshold { get; set; } = 5.0;
}
