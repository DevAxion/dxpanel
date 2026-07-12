using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

/// <summary>
/// A hosting plan authored by a reseller and offered to their own clients.
/// Limits are validated against the reseller's allocated <see cref="ResellerProfile"/>.
/// </summary>
public class ResellerPackage
{
    public int Id { get; set; }

    [Required]
    public string ResellerId { get; set; } = string.Empty;
    public ApplicationUser? Reseller { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(300)]
    public string? Description { get; set; }

    [Range(0, long.MaxValue)]
    public long DiskQuotaMB { get; set; } = 1024;

    [Range(0, long.MaxValue)]
    public long BandwidthQuotaMB { get; set; } = 10240;

    // 0 = unlimited
    [Range(0, int.MaxValue)]
    public int MaxDomains { get; set; } = 1;

    [Range(0, int.MaxValue)]
    public int MaxEmails { get; set; } = 5;

    [Range(0, int.MaxValue)]
    public int MaxDatabases { get; set; } = 2;

    [Range(0, int.MaxValue)]
    public int MaxFtpAccounts { get; set; } = 2;

    [Range(0, double.MaxValue)]
    public decimal Price { get; set; }

    public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;

    public bool IsPublic { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ApplicationUser> Clients { get; set; } = new List<ApplicationUser>();
}
