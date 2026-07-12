using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Services;

public interface IPlatformSettingsService
{
    Task<PlatformSettings> GetAsync();
    Task SaveAsync(PlatformSettings updated);
}

public class PlatformSettingsService : IPlatformSettingsService
{
    private const string CacheKey = "platform-settings";
    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;

    public PlatformSettingsService(ApplicationDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<PlatformSettings> GetAsync()
    {
        if (_cache.TryGetValue(CacheKey, out PlatformSettings? cached) && cached != null)
            return cached;

        var settings = await _db.PlatformSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1);
        if (settings == null)
        {
            settings = new PlatformSettings { Id = 1 };
            _db.PlatformSettings.Add(settings);
            await _db.SaveChangesAsync();
        }
        _cache.Set(CacheKey, settings, TimeSpan.FromMinutes(10));
        return settings;
    }

    public async Task SaveAsync(PlatformSettings updated)
    {
        var settings = await _db.PlatformSettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (settings == null)
        {
            updated.Id = 1;
            _db.PlatformSettings.Add(updated);
        }
        else
        {
            settings.PlatformName = updated.PlatformName;
            settings.LogoPath = updated.LogoPath;
            settings.DefaultCurrency = updated.DefaultCurrency;
            settings.PlatformFeePercent = updated.PlatformFeePercent;
            settings.TrialPeriodDays = updated.TrialPeriodDays;
            settings.MinPayoutAmount = updated.MinPayoutAmount;
            settings.DefaultAffiliateCommission = updated.DefaultAffiliateCommission;
            settings.TermsUrl = updated.TermsUrl;
            settings.PrivacyUrl = updated.PrivacyUrl;
            settings.MaintenanceMode = updated.MaintenanceMode;
            settings.Registration = updated.Registration;
            settings.RequireEmailVerification = updated.RequireEmailVerification;
            settings.UpdateChannel = updated.UpdateChannel;
            settings.AutoUpdate = updated.AutoUpdate;
            settings.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
        _cache.Remove(CacheKey);
    }
}
