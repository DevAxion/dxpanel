using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

public class Domain
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [Required]
    [StringLength(255)]
    [RegularExpression(@"^(?!-)[A-Za-z0-9-]{1,63}(?<!-)(\.[A-Za-z0-9-]{1,63})*\.[A-Za-z]{2,}$",
        ErrorMessage = "Enter a valid domain name.")]
    public string DomainName { get; set; } = string.Empty;

    [Required]
    [StringLength(500)]
    public string DocumentRoot { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool SslEnabled { get; set; }

    [StringLength(20)]
    public string PhpVersion { get; set; } = "8.3";

    // Phase 5 per-domain settings
    public bool ForceHttps { get; set; }
    public bool DirectoryListing { get; set; }
    public bool AutoRenewSsl { get; set; } = true;

    [StringLength(255)]
    public string? Error404Path { get; set; }

    [StringLength(255)]
    public string? Error500Path { get; set; }
}
