using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

/// <summary>
/// White-label appearance for a reseller. Applied to the panel whenever the
/// active request resolves to this reseller (by logged-in user or custom domain).
/// </summary>
public class ResellerBranding
{
    public int Id { get; set; }

    [Required]
    public string ResellerId { get; set; } = string.Empty;
    public ApplicationUser? Reseller { get; set; }

    [StringLength(100)]
    public string PanelTitle { get; set; } = "Hosting Panel";

    [StringLength(300)]
    public string? LogoPath { get; set; }

    [StringLength(300)]
    public string? FaviconPath { get; set; }

    [StringLength(20)]
    public string PrimaryColor { get; set; } = "#2563eb";

    [StringLength(20)]
    public string SecondaryColor { get; set; } = "#1e2531";

    [StringLength(20)]
    public string AccentColor { get; set; } = "#3b82f6";

    // Either a colour (#rrggbb) or an uploaded image path.
    [StringLength(300)]
    public string? LoginBackground { get; set; }

    [StringLength(300)]
    public string? FooterText { get; set; }

    [StringLength(200)]
    public string? CustomDomain { get; set; }

    [StringLength(150)]
    public string? EmailSenderName { get; set; }

    [StringLength(200)]
    public string? EmailSenderAddress { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
