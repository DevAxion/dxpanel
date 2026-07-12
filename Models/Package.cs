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

    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
}
