using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Services.Api;

/// <summary>
/// Authenticates external API callers by their X-API-Key, enforces a per-key
/// rate limit, and writes an <see cref="ApiRequestLog"/> row per request.
/// </summary>
public interface IApiAuthService
{
    Task<(ApplicationUser? User, ApiKey? Key)> AuthenticateAsync(string? rawKey);
    bool RateLimitOk(string prefix);
    Task LogAsync(ApiKey? key, string method, string path, string integration, int status, string? ip, string? summary);
}

public class ApiAuthService : IApiAuthService
{
    private readonly ApplicationDbContext _db;
    private readonly ISecretHasher _hasher;
    private readonly IRateLimitService _rateLimit;

    public ApiAuthService(ApplicationDbContext db, ISecretHasher hasher, IRateLimitService rateLimit)
    {
        _db = db;
        _hasher = hasher;
        _rateLimit = rateLimit;
    }

    public async Task<(ApplicationUser? User, ApiKey? Key)> AuthenticateAsync(string? rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey) || rawKey.Length < 16) return (null, null);
        var prefix = rawKey[..16];

        var candidates = await _db.ApiKeys
            .Where(k => k.Prefix == prefix && k.IsActive)
            .ToListAsync();

        var match = candidates.FirstOrDefault(k => _hasher.Verify(rawKey, k.KeyHash));
        if (match == null) return (null, null);

        match.LastUsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == match.UserId);
        return (user, match);
    }

    public bool RateLimitOk(string prefix) => _rateLimit.IsAllowed(prefix, "api", maxPerMinute: 100);

    public async Task LogAsync(ApiKey? key, string method, string path, string integration, int status, string? ip, string? summary)
    {
        _db.ApiRequestLogs.Add(new ApiRequestLog
        {
            ApiKeyId = key?.Id,
            KeyPrefix = key?.Prefix,
            Method = method,
            Path = path,
            Integration = integration,
            StatusCode = status,
            Ip = ip,
            Summary = summary,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }
}
