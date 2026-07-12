using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

/// <summary>
/// Single-row (Id = 1) configuration for the public marketing frontend.
/// Editable by the SuperAdmin under /Admin/Frontend.
/// </summary>
public class FrontendSettings
{
    public int Id { get; set; } = 1;

    // Identity
    [StringLength(100)] public string SiteName { get; set; } = "Professional Web Hosting";
    [StringLength(200)] public string Tagline { get; set; } = "Fast, reliable and secure web hosting.";
    /// <summary>Optional logo shown in the navbar/footer instead of the company name.</summary>
    [StringLength(400)] public string? LogoPath { get; set; }

    // Hero
    [StringLength(200)] public string HeroHeadline { get; set; } = "Professional Web Hosting";
    [StringLength(400)] public string HeroSubheadline { get; set; } =
        "Fast, reliable and secure hosting for your websites and applications.";
    [StringLength(60)] public string HeroCtaPrimaryText { get; set; } = "Get Started";
    [StringLength(60)] public string HeroCtaSecondaryText { get; set; } = "View Plans";

    // Company / contact
    [StringLength(120)] public string? ContactEmail { get; set; } = "hello@example.com";
    [StringLength(60)] public string? ContactPhone { get; set; } = "+1 (555) 010-2030";
    [StringLength(300)] public string? ContactAddress { get; set; } = "123 Cloud Street, Internet City";
    public string? GoogleMapsEmbed { get; set; }

    [StringLength(4000)] public string? AboutContent { get; set; } =
        "We provide fast, reliable and secure web hosting for businesses and individuals, backed by friendly round-the-clock support.";
    [StringLength(500)] public string? MissionStatement { get; set; } =
        "To make professional web hosting accessible to everyone.";

    // Social
    [StringLength(200)] public string? SocialFacebook { get; set; }
    [StringLength(200)] public string? SocialTwitter { get; set; }
    [StringLength(200)] public string? SocialInstagram { get; set; }
    [StringLength(200)] public string? SocialLinkedin { get; set; }
    [StringLength(200)] public string? SocialGithub { get; set; }

    // Appearance
    [StringLength(9)] public string PrimaryColor { get; set; } = "#3b82f6";
    [StringLength(9)] public string SecondaryColor { get; set; } = "#111827";
    public string? CustomCss { get; set; }
    public string? CustomJs { get; set; }

    // Marketing / analytics
    [StringLength(60)] public string? GoogleAnalyticsId { get; set; }
    [StringLength(60)] public string? FacebookPixelId { get; set; }
    public string? LiveChatCode { get; set; }
    [StringLength(500)] public string? CookieConsentText { get; set; } =
        "We use cookies to improve your experience. By continuing you agree to our use of cookies.";

    // SEO
    [StringLength(200)] public string? MetaDescription { get; set; } =
        "Fast, reliable and secure web hosting for your websites and applications. Domains, email, databases, DNS and free SSL.";
    public string RobotsTxt { get; set; } = "User-agent: *\nAllow: /\n";

    // Guarantees
    [Range(0, 365)] public int MoneyBackDays { get; set; } = 30;

    /// <summary>Show a "Powered by" credit in the public footer. Off by default (full white-label).</summary>
    public bool ShowPoweredBy { get; set; }

    // Maintenance
    public bool MaintenanceMode { get; set; }
    [StringLength(500)] public string? MaintenanceMessage { get; set; } =
        "We're performing scheduled maintenance and will be back shortly.";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class Testimonial
{
    public int Id { get; set; }
    [Required, StringLength(120)] public string Name { get; set; } = string.Empty;
    [StringLength(120)] public string? Company { get; set; }
    [StringLength(400)] public string? PhotoUrl { get; set; }
    [Required, StringLength(1000)] public string Content { get; set; } = string.Empty;
    [Range(1, 5)] public int Rating { get; set; } = 5;
    public bool IsPublished { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class FeatureItem
{
    public int Id { get; set; }
    [Required, StringLength(60)] public string Icon { get; set; } = "bi-stars";
    [Required, StringLength(120)] public string Title { get; set; } = string.Empty;
    [StringLength(400)] public string Description { get; set; } = string.Empty;
    public bool IsPublished { get; set; } = true;
    public int SortOrder { get; set; }
}

public class StatCounter
{
    public int Id { get; set; }
    [Required, StringLength(80)] public string Label { get; set; } = string.Empty;
    public long Value { get; set; }
    [StringLength(16)] public string? Suffix { get; set; }
    [StringLength(60)] public string Icon { get; set; } = "bi-graph-up-arrow";
    public int SortOrder { get; set; }
}

/// <summary>Anonymous-friendly contact submission (avoids the ticket FK on ApplicationUser).</summary>
public class ContactMessage
{
    public int Id { get; set; }
    [Required, StringLength(120)] public string Name { get; set; } = string.Empty;
    [Required, EmailAddress, StringLength(200)] public string Email { get; set; } = string.Empty;
    [StringLength(200)] public string? Subject { get; set; }
    [Required, StringLength(4000)] public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
