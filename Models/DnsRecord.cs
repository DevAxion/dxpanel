using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

public enum DnsRecordType
{
    A,
    AAAA,
    CNAME,
    MX,
    TXT,
    NS,
    SRV
}

public class DnsRecord
{
    public int Id { get; set; }

    [Required]
    public int ZoneId { get; set; }
    public DnsZone? Zone { get; set; }

    public DnsRecordType Type { get; set; } = DnsRecordType.A;

    [Required]
    [StringLength(255)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(1000)]
    public string Value { get; set; } = string.Empty;

    public int TTL { get; set; } = 3600;

    // Used for MX and SRV records
    public int? Priority { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
