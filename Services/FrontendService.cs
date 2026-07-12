using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Services;

public interface IFrontendService
{
    Task<FrontendSettings> GetSettingsAsync();
    Task SaveSettingsAsync(FrontendSettings updated);
    void Invalidate();
}

public class FrontendService : IFrontendService
{
    private const string CacheKey = "frontend-settings";
    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;

    public FrontendService(ApplicationDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<FrontendSettings> GetSettingsAsync()
    {
        if (_cache.TryGetValue(CacheKey, out FrontendSettings? cached) && cached != null)
            return cached;

        var settings = await _db.FrontendSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1);
        if (settings == null)
        {
            settings = new FrontendSettings { Id = 1 };
            _db.FrontendSettings.Add(settings);
            await _db.SaveChangesAsync();
        }
        _cache.Set(CacheKey, settings, TimeSpan.FromMinutes(10));
        return settings;
    }

    public async Task SaveSettingsAsync(FrontendSettings updated)
    {
        var settings = await _db.FrontendSettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (settings == null)
        {
            updated.Id = 1;
            _db.FrontendSettings.Add(updated);
        }
        else
        {
            _db.Entry(settings).CurrentValues.SetValues(updated);
            settings.Id = 1;
            settings.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
        _cache.Remove(CacheKey);
    }

    public void Invalidate() => _cache.Remove(CacheKey);
}

public static class Slug
{
    public static string From(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return Guid.NewGuid().ToString("n")[..8];
        var normalized = input.Trim().ToLowerInvariant();
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (char.IsWhiteSpace(c) || c == '-' || c == '_') sb.Append('-');
        }
        var slug = Regex.Replace(sb.ToString(), "-{2,}", "-").Trim('-');
        return string.IsNullOrEmpty(slug) ? Guid.NewGuid().ToString("n")[..8] : slug;
    }
}
