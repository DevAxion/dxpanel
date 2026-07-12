using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

/// <summary>
/// Resource allocation and feature grants a SuperAdmin assigns to a reseller.
/// A user becomes a reseller when they hold the Reseller role and have a profile.
/// </summary>
public class ResellerProfile
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [Required]
    [StringLength(150)]
    public string CompanyName { get; set; } = string.Empty;

    // Total resources the reseller may distribute across their clients (0 = unlimited).
    [Range(0, long.MaxValue)]
    public long DiskQuotaMB { get; set; } = 10240;

    [Range(0, long.MaxValue)]
    public long BandwidthQuotaMB { get; set; } = 102400;

    [Range(0, int.MaxValue)]
    public int MaxClients { get; set; } = 10;

    [Range(0, int.MaxValue)]
    public int MaxDomains { get; set; } = 50;

    // Feature grants
    public bool AllowEmail { get; set; } = true;
    public bool AllowDns { get; set; } = true;
    public bool AllowBackups { get; set; } = true;
    public bool AllowCustomPhp { get; set; } = true;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
