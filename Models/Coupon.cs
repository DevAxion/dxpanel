using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

public class Coupon
{
    public int Id { get; set; }

    [Required]
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;

    [Range(1, 100)]
    public int DiscountPercent { get; set; }

    // 0 = unlimited
    public int MaxUses { get; set; }
    public int UsedCount { get; set; }

    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;

    [StringLength(100)]
    public string? StripeCouponId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsValid =>
        IsActive
        && (ExpiresAt == null || ExpiresAt > DateTime.UtcNow)
        && (MaxUses == 0 || UsedCount < MaxUses);
}
