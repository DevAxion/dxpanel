using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

public enum BillingCycle
{
    Monthly,
    Annual
}

public class Plan
{
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    [Range(0, double.MaxValue)]
    public decimal Price { get; set; }

    [StringLength(10)]
    public string Currency { get; set; } = "usd";

    public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;

    public long DiskQuotaMB { get; set; }
    public long BandwidthQuotaMB { get; set; }
    public int MaxDomains { get; set; }
    public int MaxEmails { get; set; }
    public int MaxDatabases { get; set; }
    public int MaxFtpAccounts { get; set; }

    public bool IsActive { get; set; } = true;

    [StringLength(100)]
    public string? StripeProductId { get; set; }

    [StringLength(100)]
    public string? StripePriceId { get; set; }

    // Annual price with the standard 20% discount applied (helper for the UI).
    public decimal AnnualPrice => Math.Round(Price * 12 * 0.8m, 2);
}
