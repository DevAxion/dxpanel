using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

public class EmailForwarder
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
    public string Source { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(255)]
    public string Destination { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
