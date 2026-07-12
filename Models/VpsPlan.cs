using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

public class VpsPlan
{
    public int Id { get; set; }

    [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
    [StringLength(300)] public string? Description { get; set; }

    public int CpuCores { get; set; }
    public int RamMB { get; set; }
    public int DiskGB { get; set; }
    public int BandwidthGB { get; set; }

    public decimal Price { get; set; }
    public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;

    [StringLength(80)] public string Location { get; set; } = "Frankfurt, DE";

    /// <summary>Comma-separated OS slugs, e.g. "ubuntu,debian,centos".</summary>
    [StringLength(200)] public string OsOptions { get; set; } = "ubuntu,debian,centos";

    public bool IsPopular { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    // ---- Phase 12: Proxmox placement ----

    /// <summary>Preferred Proxmox node this plan provisions onto (null = auto-pick least loaded).</summary>
    public int? NodeId { get; set; }

    /// <summary>Comma-separated VpsTemplate.Id values offered for this plan (empty = all templates on the node).</summary>
    [StringLength(200)] public string TemplateIds { get; set; } = string.Empty;

    public IEnumerable<int> TemplateIdList =>
        TemplateIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var v) ? v : 0).Where(v => v > 0);

    // Annual price with the standard 20% discount applied (helper for the UI).
    public decimal AnnualPrice => Math.Round(Price * 12 * 0.8m, 2);

    public IEnumerable<string> OsList =>
        OsOptions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
