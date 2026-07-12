using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

public enum ResellerBillingModel { Prepaid, Postpaid }
public enum ResellerTransactionType { Credit, Debit, Fee, Payout }
public enum ResellerInvoiceStatus { Unpaid, Paid, Void }

/// <summary>How a reseller is billed by the platform and their auto-top-up prefs.</summary>
public class ResellerBillingConfig
{
    public int Id { get; set; }

    [Required] public string ResellerId { get; set; } = string.Empty;
    public ApplicationUser? Reseller { get; set; }

    public ResellerBillingModel Model { get; set; } = ResellerBillingModel.Prepaid;

    [Range(0, 100)] public decimal PlatformFeePercent { get; set; } = 10m;
    [Range(0, 1_000_000)] public decimal MinPayoutAmount { get; set; } = 50m;

    [StringLength(3)] public string Currency { get; set; } = "usd";

    // Prepaid auto top-up
    public bool AutoTopUpEnabled { get; set; }
    [Range(0, 1_000_000)] public decimal AutoTopUpThreshold { get; set; } = 10m;
    [Range(0, 1_000_000)] public decimal AutoTopUpAmount { get; set; } = 50m;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A ledger entry against a reseller's platform balance.</summary>
public class ResellerTransaction
{
    public int Id { get; set; }

    [Required] public string ResellerId { get; set; } = string.Empty;
    public ApplicationUser? Reseller { get; set; }

    public ResellerTransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public decimal Balance { get; set; } // running balance after this entry

    [StringLength(300)] public string? Description { get; set; }
    [StringLength(100)] public string? ReferenceId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A platform invoice issued to a reseller (postpaid / periodic).</summary>
public class ResellerInvoice
{
    public int Id { get; set; }

    [Required] public string ResellerId { get; set; } = string.Empty;
    public ApplicationUser? Reseller { get; set; }

    [StringLength(40)] public string Number { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public ResellerInvoiceStatus Status { get; set; } = ResellerInvoiceStatus.Unpaid;

    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
