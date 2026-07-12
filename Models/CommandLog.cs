using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

public class CommandLog
{
    public int Id { get; set; }

    [Required]
    public string Command { get; set; } = string.Empty;

    public string Output { get; set; } = string.Empty;

    public int ExitCode { get; set; }

    /// <summary>True when the command was simulated (not actually executed).</summary>
    public bool Simulated { get; set; }

    [StringLength(100)]
    public string? Service { get; set; }

    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;

    [StringLength(256)]
    public string? TriggeredBy { get; set; }
}
