using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

public enum AddonType
{
    ExtraDisk,        // Value = extra MB
    ExtraBandwidth,   // Value = extra MB
    ExtraEmail,       // Value = extra mailbox count
    ExtraDatabase,    // Value = extra database count
    DedicatedIp,      // Value = number of IPs
    PremiumSsl,       // Value = number of certificates
    DailyBackup,      // feature flag
    PrioritySupport   // feature flag
}

/// <summary>An add-on service a client can purchase on top of a hosting plan.</summary>
public class Addon
{
    public int Id { get; set; }

    [Required, StringLength(120)] public string Name { get; set; } = string.Empty;
    [StringLength(400)] public string Description { get; set; } = string.Empty;

    public AddonType Type { get; set; }

    /// <summary>Magnitude of the add-on (MB for disk/bandwidth, count for emails/DBs/IPs, 0 for flags).</summary>
    public long Value { get; set; }

    [Range(0, double.MaxValue)] public decimal Price { get; set; }
    [StringLength(10)] public string Currency { get; set; } = "usd";
    public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>An add-on purchased by a client.</summary>
public class ClientAddon
{
    public int Id { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public int AddonId { get; set; }
    public Addon? Addon { get; set; }

    public int Quantity { get; set; } = 1;
    public DateTime PurchasedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum CartItemType
{
    Plan,
    Addon,
    Domain,
    Vps,
    Reseller
}

public enum ClientServiceType
{
    Vps,
    Reseller
}

/// <summary>
/// A purchased non-shared-hosting service (VPS or reseller package). Shared hosting
/// uses <see cref="Subscription"/>; this records everything else so it shows up under
/// "My Services" too.
/// </summary>
public class ClientService
{
    public int Id { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public ClientServiceType Type { get; set; }

    /// <summary>VpsPlan.Id or ResellerPackage.Id this was ordered from.</summary>
    public int ReferenceId { get; set; }

    [Required, StringLength(120)] public string Name { get; set; } = string.Empty;
    [StringLength(300)] public string ResourceSummary { get; set; } = string.Empty;

    [Range(0, double.MaxValue)] public decimal Price { get; set; }
    [StringLength(10)] public string Currency { get; set; } = "usd";
    public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;

    public DateTime CurrentPeriodStart { get; set; } = DateTime.UtcNow;
    public DateTime CurrentPeriodEnd { get; set; } = DateTime.UtcNow.AddMonths(1);
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CancelledAt { get; set; }
}

/// <summary>A line item in a client's shopping cart.</summary>
public class CartItem
{
    public int Id { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;

    public CartItemType Type { get; set; }

    /// <summary>Plan/Addon id (0 for a domain, which uses <see cref="DomainName"/>).</summary>
    public int ReferenceId { get; set; }

    [StringLength(200)] public string? DomainName { get; set; }

    /// <summary>Human label shown in the cart.</summary>
    [StringLength(200)] public string Label { get; set; } = string.Empty;

    public int Quantity { get; set; } = 1;
    [Range(0, double.MaxValue)] public decimal Price { get; set; }
    [StringLength(10)] public string Currency { get; set; } = "usd";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public decimal LineTotal => Price * Quantity;
}

public enum DomainRegistrationStatus
{
    Pending,
    Active,
    Failed
}

/// <summary>A domain registered through the store (registrar integration is mocked in simulation).</summary>
public class DomainRegistration
{
    public int Id { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [Required, StringLength(200)] public string DomainName { get; set; } = string.Empty;
    [StringLength(20)] public string Tld { get; set; } = string.Empty;

    [Range(0, double.MaxValue)] public decimal Price { get; set; }
    [StringLength(10)] public string Currency { get; set; } = "usd";

    [StringLength(60)] public string Registrar { get; set; } = "Simulated Registrar";
    public DomainRegistrationStatus Status { get; set; } = DomainRegistrationStatus.Active;

    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddYears(1);
    public bool AutoRenew { get; set; } = true;
}
