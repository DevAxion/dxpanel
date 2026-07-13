using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

public class Package
{
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Range(0, long.MaxValue)]
    public long DiskQuotaMB { get; set; }

    [Range(0, long.MaxValue)]
    public long BandwidthQuotaMB { get; set; }

    // 0 = unlimited
    [Range(0, int.MaxValue)]
    public int MaxDomains { get; set; }

    [Range(0, int.MaxValue)]
    public int MaxEmails { get; set; }

    [Range(0, int.MaxValue)]
    public int MaxDatabases { get; set; }

    [Range(0, int.MaxValue)]
    public int MaxFtpAccounts { get; set; }

    [Range(0, int.MaxValue)]
    public int MaxCronJobs { get; set; } = 10;

    // Retained on-panel backups this plan allows (0 = unlimited).
    // Starter = 1, Professional = 7, Business = 30.
    [Range(0, int.MaxValue)]
    public int MaxBackups { get; set; } = 1;

    [Range(0, double.MaxValue)]
    [DataType(DataType.Currency)]
    public decimal Price { get; set; }

    // ---- Feature flags ----
    // Gate which Client sidebar sections a customer on this package can see.
    // Default true so existing packages keep their current (show-everything) behavior.
    public bool AllowVpsStore { get; set; } = true;
    public bool AllowAppHosting { get; set; } = true;
    public bool AllowCloudflare { get; set; } = true;
    public bool AllowAdvancedMail { get; set; } = true;
    public bool AllowDeveloperTools { get; set; } = true;

    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
}
