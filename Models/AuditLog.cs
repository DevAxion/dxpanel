namespace SRXPanel.Models;

public class AuditLog
{
    public int Id { get; set; }
    public string? UserId { get; set; }
    public string UserName { get; set; } = "system";
    public string Action { get; set; } = string.Empty;
    public string Entity { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? Details { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
