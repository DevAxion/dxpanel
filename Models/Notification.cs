using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

public enum NotificationType
{
    Info,
    Warning,
    Error,
    Success
}

public class Notification
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(1000)]
    public string Message { get; set; } = string.Empty;

    public NotificationType Type { get; set; } = NotificationType.Info;

    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // De-dupe key for auto-generated notifications (e.g. "ssl-expiry-5")
    [StringLength(100)]
    public string? DedupeKey { get; set; }
}
