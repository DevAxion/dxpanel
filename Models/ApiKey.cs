using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

public class ApiKey
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    // Visible prefix (e.g. "srx_live_ab12") for identification; full key shown once.
    [StringLength(32)]
    public string Prefix { get; set; } = string.Empty;

    // BCrypt hash of the full key — the plaintext is never stored.
    [Required]
    public string KeyHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
    public bool IsActive { get; set; } = true;
}
