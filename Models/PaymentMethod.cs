using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

/// <summary>
/// Minimal card metadata only — full card numbers are NEVER stored (PCI).
/// Only brand + last4 + expiry come back from Stripe.
/// </summary>
public class PaymentMethod
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [StringLength(100)]
    public string StripePaymentMethodId { get; set; } = string.Empty;

    [StringLength(30)]
    public string Brand { get; set; } = string.Empty;

    [StringLength(4)]
    public string Last4 { get; set; } = string.Empty;

    public int ExpMonth { get; set; }
    public int ExpYear { get; set; }

    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
