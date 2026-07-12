using Microsoft.Extensions.Caching.Memory;

namespace SRXPanel.Services;

/// <summary>
/// Simple per-user sliding-window rate limiter for create operations
/// (max 10 per minute per user by default).
/// </summary>
public interface IRateLimitService
{
    bool IsAllowed(string userId, string action, int maxPerMinute = 10);
}

public class RateLimitService : IRateLimitService
{
    private readonly IMemoryCache _cache;
    private static readonly object Lock = new();

    public RateLimitService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public bool IsAllowed(string userId, string action, int maxPerMinute = 10)
    {
        var key = $"ratelimit:{action}:{userId}";

        lock (Lock)
        {
            var timestamps = _cache.GetOrCreate(key, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
                return new List<DateTime>();
            })!;

            var cutoff = DateTime.UtcNow.AddMinutes(-1);
            timestamps.RemoveAll(t => t < cutoff);

            if (timestamps.Count >= maxPerMinute)
            {
                return false;
            }

            timestamps.Add(DateTime.UtcNow);
            _cache.Set(key, timestamps, TimeSpan.FromMinutes(2));
            return true;
        }
    }
}
