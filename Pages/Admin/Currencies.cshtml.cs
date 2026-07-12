using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;

namespace SRXPanel.Pages.Admin;

public class CurrenciesModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IPlatformSettingsService _platform;

    public CurrenciesModel(ApplicationDbContext db, IPlatformSettingsService platform)
    {
        _db = db;
        _platform = platform;
    }

    public List<Currency> Currencies { get; set; } = new();
    public List<ExchangeRate> Rates { get; set; } = new();
    public string BaseCurrency { get; set; } = "usd";

    private async Task LoadAsync()
    {
        Currencies = await _db.Currencies.OrderBy(c => c.Code).ToListAsync();
        Rates = await _db.ExchangeRates.OrderBy(r => r.FromCurrency).ToListAsync();
        BaseCurrency = (await _platform.GetAsync()).DefaultCurrency;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAddCurrencyAsync(string code, string name, string symbol)
    {
        code = (code ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(code)) { TempData["Error"] = "Code required."; return RedirectToPage(); }
        if (!await _db.Currencies.AnyAsync(c => c.Code == code))
        {
            _db.Currencies.Add(new Currency { Code = code, Name = name ?? code.ToUpper(), Symbol = string.IsNullOrWhiteSpace(symbol) ? code.ToUpper() : symbol, IsEnabled = true });
            await _db.SaveChangesAsync();
        }
        TempData["Success"] = "Currency saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var cur = await _db.Currencies.FindAsync(id);
        if (cur != null) { cur.IsEnabled = !cur.IsEnabled; await _db.SaveChangesAsync(); }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetRateAsync(string from, string to, decimal rate)
    {
        from = from.ToLowerInvariant(); to = to.ToLowerInvariant();
        var existing = await _db.ExchangeRates.FirstOrDefaultAsync(r => r.FromCurrency == from && r.ToCurrency == to);
        if (existing == null)
            _db.ExchangeRates.Add(new ExchangeRate { FromCurrency = from, ToCurrency = to, Rate = rate, UpdatedAt = DateTime.UtcNow });
        else { existing.Rate = rate; existing.UpdatedAt = DateTime.UtcNow; }
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Rate {from.ToUpper()}→{to.ToUpper()} set to {rate}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostBaseCurrencyAsync(string baseCurrency)
    {
        var settings = await _platform.GetAsync();
        settings.DefaultCurrency = baseCurrency.ToLowerInvariant();
        await _platform.SaveAsync(settings);
        TempData["Success"] = "Base currency updated.";
        return RedirectToPage();
    }
}
