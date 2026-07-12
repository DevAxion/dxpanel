using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

public class FtpAccount
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public int? DomainId { get; set; }
    public Domain? Domain { get; set; }

    [Required]
    [StringLength(64)]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [Required]
    [StringLength(500)]
    public string HomeDirectory { get; set; } = string.Empty;

    // 0 = unlimited
    public long QuotaMB { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
