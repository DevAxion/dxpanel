using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

/// <summary>A directional conversion rate between two currencies.</summary>
public class ExchangeRate
{
    public int Id { get; set; }

    [Required, StringLength(3)] public string FromCurrency { get; set; } = "usd";
    [Required, StringLength(3)] public string ToCurrency { get; set; } = "usd";

    public decimal Rate { get; set; } = 1m;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A currency the platform supports and whether it is enabled for use.</summary>
public class Currency
{
    public int Id { get; set; }

    [Required, StringLength(3)] public string Code { get; set; } = "usd";
    [StringLength(40)] public string Name { get; set; } = string.Empty;
    [StringLength(5)] public string Symbol { get; set; } = "$";
    public bool IsEnabled { get; set; } = true;
}
