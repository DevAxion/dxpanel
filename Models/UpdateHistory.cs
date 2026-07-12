using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

public enum UpdateStatus
{
    Success,
    Failed,
    InProgress
}

/// <summary>
/// Records every panel version transition (install / update / rollback) so the
/// /Admin/Updates page can show an audit trail. Simulation-safe: on a dev box the
/// "update" is recorded without actually pulling git or restarting the service.
/// </summary>
public class UpdateHistory
{
    public int Id { get; set; }

    [Required, StringLength(32)]
    public string FromVersion { get; set; } = string.Empty;

    [Required, StringLength(32)]
    public string ToVersion { get; set; } = string.Empty;

    [StringLength(32)]
    public string Channel { get; set; } = "stable";

    public UpdateStatus Status { get; set; } = UpdateStatus.Success;

    /// <summary>Short changelog / notes for this update.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>True when the update was simulated (dev/Windows) rather than actually applied.</summary>
    public bool Simulated { get; set; }

    [StringLength(256)]
    public string? TriggeredBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
