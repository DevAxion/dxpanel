using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

public class DnsZone
{
    public int Id { get; set; }

    [Required]
    public int DomainId { get; set; }
    public Domain? Domain { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<DnsRecord> Records { get; set; } = new List<DnsRecord>();
}
