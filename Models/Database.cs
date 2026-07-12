using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

public class Database
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public int? DomainId { get; set; }
    public Domain? Domain { get; set; }

    [Required]
    [StringLength(64)]
    public string DbName { get; set; } = string.Empty;

    [Required]
    [StringLength(64)]
    public string DbUser { get; set; } = string.Empty;

    [Required]
    public string DbPasswordHash { get; set; } = string.Empty;

    // Size in MB
    public double DbSize { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}
