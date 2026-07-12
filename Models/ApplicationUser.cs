using Microsoft.AspNetCore.Identity;

namespace SRXPanel.Models;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public long DiskQuotaMB { get; set; } = 1024;
    public long BandwidthQuotaMB { get; set; } = 10240;
    public string? ResellerId { get; set; }
    public ApplicationUser? Reseller { get; set; }
    public int? PackageId { get; set; }
    public Package? Package { get; set; }

    // Phase 6A — reseller assignment. A client provisioned by a reseller uses
    // one of that reseller's own packages instead of an admin Package.
    public int? ResellerPackageId { get; set; }
    public ResellerPackage? ResellerPackage { get; set; }

    // Set when a client/reseller account is suspended, for display in the panel.
    public string? SuspensionReason { get; set; }

    // Phase 6B — billing/affiliate
    public string DisplayCurrency { get; set; } = "usd";
    public int? ReferredByAffiliateId { get; set; }

    // Profile (Phase 5 self-service)
    public string TimeZone { get; set; } = "UTC";
    public string Language { get; set; } = "en";

    // Notification preferences
    public bool NotifyInvoices { get; set; } = true;
    public bool NotifySslExpiry { get; set; } = true;
    public bool NotifyDiskUsage { get; set; } = true;
    public bool NotifySupport { get; set; } = true;

    public ICollection<Domain> Domains { get; set; } = new List<Domain>();
    public ICollection<Database> Databases { get; set; } = new List<Database>();
    public ICollection<FtpAccount> FtpAccounts { get; set; } = new List<FtpAccount>();
    public ICollection<EmailAccount> EmailAccounts { get; set; } = new List<EmailAccount>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
}
