using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

/// <summary>
/// Stripe Connect / own-keys configuration plus tax settings for a reseller.
/// </summary>
public class ResellerPaymentSettings
{
    public int Id { get; set; }

    [Required] public string ResellerId { get; set; } = string.Empty;
    public ApplicationUser? Reseller { get; set; }

    // Stripe Connect (Express) account.
    public string? StripeConnectAccountId { get; set; }
    public bool ConnectOnboarded { get; set; }

    // Alternative: reseller's own Stripe keys (bypass Connect).
    public bool UseOwnKeys { get; set; }
    public string? OwnPublishableKey { get; set; }
    public string? OwnSecretKey { get; set; }

    [StringLength(200)] public string AcceptedMethods { get; set; } = "card";
    [StringLength(3)] public string Currency { get; set; } = "usd";

    // Tax configuration
    [Range(0, 100)] public decimal TaxRatePercent { get; set; }
    [StringLength(20)] public string TaxLabel { get; set; } = "VAT";
    [StringLength(60)] public string? TaxNumber { get; set; }
    public bool ShowTaxNumberOnInvoice { get; set; } = true;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsConnected => ConnectOnboarded || (UseOwnKeys && !string.IsNullOrEmpty(OwnSecretKey));
}

/// <summary>Company + invoice presentation details for a reseller's own invoices.</summary>
public class ResellerInvoiceSettings
{
    public int Id { get; set; }

    [Required] public string ResellerId { get; set; } = string.Empty;
    public ApplicationUser? Reseller { get; set; }

    [StringLength(150)] public string? CompanyName { get; set; }
    [StringLength(400)] public string? CompanyAddress { get; set; }
    [StringLength(60)] public string? TaxNumber { get; set; }

    [StringLength(20)] public string InvoicePrefix { get; set; } = "INV-";
    public int NextNumber { get; set; } = 1;

    [StringLength(300)] public string? LogoPath { get; set; }
    [StringLength(600)] public string? PaymentTerms { get; set; }
    [StringLength(600)] public string? FooterNotes { get; set; }
    [StringLength(400)] public string? BankDetails { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
