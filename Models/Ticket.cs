using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

public enum TicketStatus
{
    Open,
    InProgress,
    Closed
}

public enum TicketPriority
{
    Low,
    Normal,
    High,
    Urgent
}

public class Ticket
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [Required]
    [StringLength(200)]
    public string Subject { get; set; } = string.Empty;

    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public TicketPriority Priority { get; set; } = TicketPriority.Normal;

    // Staff member the ticket is assigned to (nullable)
    public string? AssignedToId { get; set; }
    public ApplicationUser? AssignedTo { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<TicketReply> Replies { get; set; } = new List<TicketReply>();
}

public class TicketReply
{
    public int Id { get; set; }

    [Required]
    public int TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [Required]
    [StringLength(5000)]
    public string Message { get; set; } = string.Empty;

    public bool IsStaff { get; set; }

    // Comma-separated relative paths of uploaded attachments
    [StringLength(1000)]
    public string? Attachments { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CannedResponse
{
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(5000)]
    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
