using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

public enum RegistrationMode { Open, InviteOnly, Disabled }

/// <summary>
/// Single-row platform-wide configuration editable by the SuperAdmin.
/// Id is fixed to 1 (see DbSeeder).
/// </summary>
public class PlatformSettings
{
    public int Id { get; set; } = 1;

    [StringLength(100)] public string PlatformName { get; set; } = "SRXPanel";
    [StringLength(300)] public string? LogoPath { get; set; }

    [StringLength(3)] public string DefaultCurrency { get; set; } = "usd";
    [Range(0, 100)] public decimal PlatformFeePercent { get; set; } = 10m;
    [Range(0, 365)] public int TrialPeriodDays { get; set; } = 14;
    [Range(0, 1_000_000)] public decimal MinPayoutAmount { get; set; } = 50m;
    [Range(0, 100)] public decimal DefaultAffiliateCommission { get; set; } = 20m;

    [StringLength(300)] public string? TermsUrl { get; set; }
    [StringLength(300)] public string? PrivacyUrl { get; set; }

    public bool MaintenanceMode { get; set; }
    public RegistrationMode Registration { get; set; } = RegistrationMode.Open;
    public bool RequireEmailVerification { get; set; }

    // Phase 7 — version management
    [StringLength(16)] public string UpdateChannel { get; set; } = "stable";
    public bool AutoUpdate { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
