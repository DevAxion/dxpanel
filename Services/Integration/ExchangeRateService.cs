using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Services.Integration;

/// <summary>
/// Fetches and caches currency exchange rates. Rates are pulled from the free
/// exchangerate-api.com endpoint (base USD), cached in memory for 24 hours and
/// persisted to the ExchangeRate table so conversions keep working offline / in
/// simulation. A daily <see cref="RefreshExchangeRatesJob"/> keeps them fresh.
/// </summary>
public interface IExchangeRateService
{
    /// <summary>USD-based rate map ("eur" => 0.92). Cached for 24h; falls back to the DB.</summary>
    Task<IReadOnlyDictionary<string, decimal>> GetRatesAsync(CancellationToken ct = default);

    /// <summary>Converts an amount between two currency codes using USD-based rates.</summary>
    Task<decimal> ConvertAsync(decimal amount, string from, string to, CancellationToken ct = default);

    /// <summary>Currency codes for which a rate is available.</summary>
    Task<List<string>> GetSupportedCurrenciesAsync(CancellationToken ct = default);

    /// <summary>Fetches fresh rates from the API and upserts them into the ExchangeRate table.</summary>
    Task<int> RefreshAsync(CancellationToken ct = default);
}

public class ExchangeRateService : IExchangeRateService
{
    // Free, no-key endpoint. Base currency is USD.
    private const string ApiUrl = "https://api.exchangerate-api.com/v4/latest/USD";
    private const string CacheKey = "exchange-rates-usd";
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(24);

    private readonly IHttpClientFactory _httpFactory;
    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ExchangeRateService> _logger;

    public ExchangeRateService(IHttpClientFactory httpFactory, ApplicationDbContext db,
        IMemoryCache cache, ILogger<ExchangeRateService> logger)
    {
        _httpFactory = httpFactory;
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, decimal>> GetRatesAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey, out IReadOnlyDictionary<string, decimal>? cached) && cached != null)
            return cached;

        // Load persisted USD-based rates from the DB.
        var rows = await _db.ExchangeRates
            .Where(r => r.FromCurrency == "usd")
            .ToListAsync(ct);

        var map = rows.ToDictionary(r => r.ToCurrency, r => r.Rate, StringComparer.OrdinalIgnoreCase);
        map["usd"] = 1m;

        // Nothing stored yet — try a live fetch so the first caller isn't empty.
        if (rows.Count == 0)
        {
            var fetched = await FetchAsync(ct);
            if (fetched != null)
            {
                await PersistAsync(fetched, ct);
                map = new Dictionary<string, decimal>(fetched, StringComparer.OrdinalIgnoreCase) { ["usd"] = 1m };
            }
        }

        _cache.Set(CacheKey, (IReadOnlyDictionary<string, decimal>)map, CacheFor);
        return map;
    }

    public async Task<decimal> ConvertAsync(decimal amount, string from, string to, CancellationToken ct = default)
    {
        from = (from ?? "usd").ToLowerInvariant();
        to = (to ?? "usd").ToLowerInvariant();
        if (from == to) return amount;

        var rates = await GetRatesAsync(ct);
        if (!rates.TryGetValue(from, out var fromRate) || fromRate == 0) return amount;
        if (!rates.TryGetValue(to, out var toRate)) return amount;

        // USD-based: amount / fromRate = USD, × toRate = target.
        var usd = amount / fromRate;
        return Math.Round(usd * toRate, 2);
    }

    public async Task<List<string>> GetSupportedCurrenciesAsync(CancellationToken ct = default)
    {
        var rates = await GetRatesAsync(ct);
        return rates.Keys.Select(k => k.ToUpperInvariant()).OrderBy(k => k).ToList();
    }

    public async Task<int> RefreshAsync(CancellationToken ct = default)
    {
        var fetched = await FetchAsync(ct);
        if (fetched == null || fetched.Count == 0) return 0;

        var count = await PersistAsync(fetched, ct);

        var map = new Dictionary<string, decimal>(fetched, StringComparer.OrdinalIgnoreCase) { ["usd"] = 1m };
        _cache.Set(CacheKey, (IReadOnlyDictionary<string, decimal>)map, CacheFor);
        return count;
    }

    private async Task<Dictionary<string, decimal>?> FetchAsync(CancellationToken ct)
    {
        try
        {
            var client = _httpFactory.CreateClient("exchange");
            var json = await client.GetStringAsync(ApiUrl, ct);
            var payload = JsonSerializer.Deserialize<ExchangeApiResponse>(json);
            if (payload?.Rates == null || payload.Rates.Count == 0) return null;

            return payload.Rates.ToDictionary(
                kv => kv.Key.ToLowerInvariant(),
                kv => kv.Value,
                StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch exchange rates from {Url}", ApiUrl);
            return null;
        }
    }

    private async Task<int> PersistAsync(Dictionary<string, decimal> rates, CancellationToken ct)
    {
        var existing = await _db.ExchangeRates
            .Where(r => r.FromCurrency == "usd")
            .ToDictionaryAsync(r => r.ToCurrency, r => r, ct);

        var now = DateTime.UtcNow;
        foreach (var (code, rate) in rates)
        {
            if (code == "usd") continue;
            if (existing.TryGetValue(code, out var row))
            {
                row.Rate = rate;
                row.UpdatedAt = now;
            }
            else
            {
                _db.ExchangeRates.Add(new ExchangeRate
                {
                    FromCurrency = "usd",
                    ToCurrency = code,
                    Rate = rate,
                    UpdatedAt = now
                });
            }
        }

        await _db.SaveChangesAsync(ct);
        return rates.Count;
    }

    private class ExchangeApiResponse
    {
        [JsonPropertyName("base")] public string Base { get; set; } = "USD";
        [JsonPropertyName("date")] public string Date { get; set; } = string.Empty;
        [JsonPropertyName("rates")] public Dictionary<string, decimal> Rates { get; set; } = new();
    }
}

/// <summary>Refreshes exchange rates once on startup and then daily.</summary>
public class RefreshExchangeRatesJob : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RefreshExchangeRatesJob> _logger;

    public RefreshExchangeRatesJob(IServiceScopeFactory scopeFactory, ILogger<RefreshExchangeRatesJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small startup delay so migrations/seed finish first.
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<IExchangeRateService>();
                var count = await svc.RefreshAsync(stoppingToken);
                _logger.LogInformation("Refreshed {Count} exchange rates.", count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exchange rate refresh failed.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }
}
