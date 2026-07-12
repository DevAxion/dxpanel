using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

public class Subdomain
{
    public int Id { get; set; }

    [Required]
    public int DomainId { get; set; }
    public Domain? Domain { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [StringLength(63)]
    [RegularExpression("^[a-z0-9-]+$", ErrorMessage = "Use lowercase letters, numbers and hyphens only.")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(500)]
    public string DocumentRoot { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum RedirectType
{
    Permanent301,
    Temporary302,
    Alias
}

public class DomainRedirect
{
    public int Id { get; set; }

    [Required]
    public int DomainId { get; set; }
    public Domain? Domain { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [StringLength(255)]
    public string Source { get; set; } = "/";

    [Required]
    [StringLength(500)]
    public string Target { get; set; } = string.Empty;

    public RedirectType Type { get; set; } = RedirectType.Permanent301;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
