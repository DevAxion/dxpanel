namespace SRXPanel.Models;

/// <summary>
/// Records a "login as" session so impersonated activity is auditable and
/// the reseller/admin session can be restored on exit.
/// </summary>
public class ImpersonationSession
{
    public int Id { get; set; }

    public string ImpersonatorId { get; set; } = string.Empty;
    public string ImpersonatorName { get; set; } = string.Empty;

    public string TargetUserId { get; set; } = string.Empty;
    public string TargetUserName { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }

    public bool IsActive { get; set; } = true;
}
