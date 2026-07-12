using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

public enum InvoiceStatus
{
    Paid,
    Unpaid,
    Void
}

public class Invoice
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public int? SubscriptionId { get; set; }
    public Subscription? Subscription { get; set; }

    [Range(0, double.MaxValue)]
    public decimal Amount { get; set; }

    [StringLength(10)]
    public string Currency { get; set; } = "usd";

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Unpaid;

    [StringLength(100)]
    public string? StripeInvoiceId { get; set; }

    public DateTime? PaidAt { get; set; }
    public DateTime DueDate { get; set; } = DateTime.UtcNow;

    [StringLength(500)]
    public string? InvoicePdf { get; set; }

    [StringLength(50)]
    public string Number { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
