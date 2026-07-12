using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Services.Portal;

public interface IApiKeyService
{
    /// <summary>Generates a key, stores only its hash, and returns the plaintext ONCE.</summary>
    Task<(ApiKey key, string plaintext)> GenerateAsync(string userId, string name);
    Task<List<ApiKey>> ListAsync(string userId);
    Task RevokeAsync(string userId, int keyId);
}

public class ApiKeyService : IApiKeyService
{
    private readonly ApplicationDbContext _db;
    private readonly ISecretHasher _hasher;

    public ApiKeyService(ApplicationDbContext db, ISecretHasher hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    public async Task<(ApiKey key, string plaintext)> GenerateAsync(string userId, string name)
    {
        var random = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var plaintext = $"srx_live_{random}";
        var prefix = plaintext[..16];

        var key = new ApiKey
        {
            UserId = userId,
            Name = name,
            Prefix = prefix,
            KeyHash = _hasher.Hash(plaintext),
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        _db.ApiKeys.Add(key);
        await _db.SaveChangesAsync();
        return (key, plaintext);
    }

    public Task<List<ApiKey>> ListAsync(string userId) =>
        _db.ApiKeys.Where(k => k.UserId == userId).OrderByDescending(k => k.CreatedAt).ToListAsync();

    public async Task RevokeAsync(string userId, int keyId)
    {
        var key = await _db.ApiKeys.FirstOrDefaultAsync(k => k.Id == keyId && k.UserId == userId);
        if (key == null) return;
        _db.ApiKeys.Remove(key);
        await _db.SaveChangesAsync();
    }
}
