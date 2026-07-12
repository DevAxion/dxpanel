using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

public enum AffiliateReferralStatus { Pending, Approved, Paid, Rejected }
public enum AffiliatePayoutStatus { Pending, Approved, Paid, Rejected }

/// <summary>An affiliate account belonging to a client or reseller.</summary>
public class Affiliate
{
    public int Id { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [Required, StringLength(20)] public string Code { get; set; } = string.Empty;
    [Range(0, 100)] public decimal CommissionPercent { get; set; } = 20m;

    public decimal TotalEarned { get; set; }
    public decimal PendingBalance { get; set; }
    public decimal PaidBalance { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<AffiliateReferral> Referrals { get; set; } = new List<AffiliateReferral>();
}

/// <summary>A referred signup and its commission state.</summary>
public class AffiliateReferral
{
    public int Id { get; set; }

    public int AffiliateId { get; set; }
    public Affiliate? Affiliate { get; set; }

    [Required] public string ReferredUserId { get; set; } = string.Empty;
    public int? SubscriptionId { get; set; }

    public decimal CommissionAmount { get; set; }
    public AffiliateReferralStatus Status { get; set; } = AffiliateReferralStatus.Pending;

    [StringLength(60)] public string? SignupIp { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A payout request submitted by an affiliate.</summary>
public class AffiliatePayoutRequest
{
    public int Id { get; set; }

    public int AffiliateId { get; set; }
    public Affiliate? Affiliate { get; set; }

    public decimal Amount { get; set; }
    [StringLength(40)] public string PaymentMethod { get; set; } = "PayPal";
    [StringLength(300)] public string? PaymentDetails { get; set; }

    public AffiliatePayoutStatus Status { get; set; } = AffiliatePayoutStatus.Pending;
    public DateTime? ProcessedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A recorded click on a referral link (for stats + fraud detection).</summary>
public class AffiliateClick
{
    public int Id { get; set; }

    public int AffiliateId { get; set; }
    [StringLength(60)] public string? Ip { get; set; }
    [StringLength(200)] public string? Utm { get; set; }
    public bool Converted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
