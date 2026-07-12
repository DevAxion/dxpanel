using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Services.Reseller;

public interface ICurrencyService
{
    Task<List<Currency>> GetAllAsync();
    Task<List<Currency>> GetEnabledAsync();
    Task<decimal> ConvertAsync(decimal amount, string from, string to);
    string Format(decimal amount, string code);
}

public class CurrencyService : ICurrencyService
{
    private readonly ApplicationDbContext _db;

    public CurrencyService(ApplicationDbContext db) => _db = db;

    public Task<List<Currency>> GetAllAsync() => _db.Currencies.OrderBy(c => c.Code).ToListAsync();
    public Task<List<Currency>> GetEnabledAsync() => _db.Currencies.Where(c => c.IsEnabled).OrderBy(c => c.Code).ToListAsync();

    public async Task<decimal> ConvertAsync(decimal amount, string from, string to)
    {
        from = from.ToLowerInvariant();
        to = to.ToLowerInvariant();
        if (from == to) return amount;

        var direct = await _db.ExchangeRates.FirstOrDefaultAsync(r => r.FromCurrency == from && r.ToCurrency == to);
        if (direct != null) return Math.Round(amount * direct.Rate, 2);

        var inverse = await _db.ExchangeRates.FirstOrDefaultAsync(r => r.FromCurrency == to && r.ToCurrency == from);
        if (inverse != null && inverse.Rate != 0) return Math.Round(amount / inverse.Rate, 2);

        return amount; // no rate configured — pass through
    }

    public string Format(decimal amount, string code)
    {
        var symbol = code.ToLowerInvariant() switch
        {
            "usd" => "$", "eur" => "€", "gbp" => "£", "try" => "₺", "azn" => "₼",
            _ => code.ToUpperInvariant() + " "
        };
        return $"{symbol}{amount:0.00}";
    }
}
