using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

public enum SubscriptionStatus
{
    Trialing,
    Active,
    PastDue,
    Cancelled
}

public class Subscription
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public int PlanId { get; set; }
    public Plan? Plan { get; set; }

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Trialing;

    [StringLength(100)]
    public string? StripeSubscriptionId { get; set; }

    [StringLength(100)]
    public string? StripeCustomerId { get; set; }

    public DateTime CurrentPeriodStart { get; set; } = DateTime.UtcNow;
    public DateTime CurrentPeriodEnd { get; set; } = DateTime.UtcNow.AddMonths(1);

    public DateTime? TrialEndsAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    // When a past-due subscription first entered dunning (drives 7/14-day escalation).
    public DateTime? PastDueSince { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int? CouponId { get; set; }
    public Coupon? Coupon { get; set; }

    public bool IsTrial => Status == SubscriptionStatus.Trialing;
    public bool IsActiveOrTrial => Status is SubscriptionStatus.Active or SubscriptionStatus.Trialing;
}
