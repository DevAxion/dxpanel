using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

/// <summary>A client-configured outbound webhook URL and the events it subscribes to.</summary>
public class WebhookEndpoint
{
    public int Id { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [Required, StringLength(400)] public string Url { get; set; } = string.Empty;
    [StringLength(80)] public string? Secret { get; set; }

    // Event toggles
    public bool OnDomainChange { get; set; } = true;
    public bool OnEmailChange { get; set; } = true;
    public bool OnSslExpiring { get; set; } = true;
    public bool OnInvoicePaid { get; set; } = true;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastTriggeredAt { get; set; }
}
