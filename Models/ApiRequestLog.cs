using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

/// <summary>An audit record of an external API call (WHMCS/Blesta/REST).</summary>
public class ApiRequestLog
{
    public int Id { get; set; }

    public int? ApiKeyId { get; set; }
    [StringLength(32)] public string? KeyPrefix { get; set; }

    [StringLength(20)] public string Method { get; set; } = string.Empty;
    [StringLength(300)] public string Path { get; set; } = string.Empty;
    [StringLength(40)] public string Integration { get; set; } = "rest"; // rest | whmcs | blesta

    public int StatusCode { get; set; }
    [StringLength(60)] public string? Ip { get; set; }
    [StringLength(500)] public string? Summary { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
