using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Services.Reseller;

public class AffiliateStats
{
    public int Clicks { get; set; }
    public int Signups { get; set; }
    public int Conversions { get; set; }
    public decimal TotalEarned { get; set; }
    public decimal PendingBalance { get; set; }
    public decimal PaidBalance { get; set; }
}

public interface IAffiliateService
{
    Task<Affiliate?> GetByUserAsync(string userId);
    Task<Affiliate> GetOrCreateAsync(string userId, decimal defaultCommission);
    Task<Affiliate?> GetByCodeAsync(string code);
    Task RecordClickAsync(string code, string? ip, string? utm);
    Task RecordSignupAsync(int affiliateId, string referredUserId, string? ip);
    Task RecordCommissionAsync(int affiliateId, string referredUserId, int? subscriptionId, decimal amount);
    Task<AffiliateStats> GetStatsAsync(int affiliateId);
    Task<AffiliatePayoutRequest?> RequestPayoutAsync(int affiliateId, decimal minPayout, string method, string? details);
    Task<List<string>> DetectFraudAsync(int affiliateId);
}

public class AffiliateService : IAffiliateService
{
    private readonly ApplicationDbContext _db;

    public AffiliateService(ApplicationDbContext db) => _db = db;

    public Task<Affiliate?> GetByUserAsync(string userId) =>
        _db.Affiliates.FirstOrDefaultAsync(a => a.UserId == userId);

    public async Task<Affiliate> GetOrCreateAsync(string userId, decimal defaultCommission)
    {
        var aff = await _db.Affiliates.FirstOrDefaultAsync(a => a.UserId == userId);
        if (aff != null) return aff;

        aff = new Affiliate
        {
            UserId = userId,
            Code = await UniqueCodeAsync(),
            CommissionPercent = defaultCommission,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _db.Affiliates.Add(aff);
        await _db.SaveChangesAsync();
        return aff;
    }

    public Task<Affiliate?> GetByCodeAsync(string code) =>
        _db.Affiliates.FirstOrDefaultAsync(a => a.Code == code && a.IsActive);

    public async Task RecordClickAsync(string code, string? ip, string? utm)
    {
        var aff = await GetByCodeAsync(code);
        if (aff == null) return;
        _db.AffiliateClicks.Add(new AffiliateClick { AffiliateId = aff.Id, Ip = ip, Utm = utm, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
    }

    public async Task RecordSignupAsync(int affiliateId, string referredUserId, string? ip)
    {
        if (await _db.AffiliateReferrals.AnyAsync(r => r.ReferredUserId == referredUserId)) return;
        _db.AffiliateReferrals.Add(new AffiliateReferral
        {
            AffiliateId = affiliateId,
            ReferredUserId = referredUserId,
            Status = AffiliateReferralStatus.Pending,
            SignupIp = ip,
            CreatedAt = DateTime.UtcNow
        });
        // Mark most recent matching click as converted.
        var click = await _db.AffiliateClicks
            .Where(c => c.AffiliateId == affiliateId && !c.Converted)
            .OrderByDescending(c => c.Id).FirstOrDefaultAsync();
        if (click != null) click.Converted = true;
        await _db.SaveChangesAsync();
    }

    public async Task RecordCommissionAsync(int affiliateId, string referredUserId, int? subscriptionId, decimal amount)
    {
        var referral = await _db.AffiliateReferrals
            .FirstOrDefaultAsync(r => r.AffiliateId == affiliateId && r.ReferredUserId == referredUserId);
        var aff = await _db.Affiliates.FindAsync(affiliateId);
        if (aff == null) return;

        var commission = Math.Round(amount * aff.CommissionPercent / 100m, 2);
        if (referral == null)
        {
            referral = new AffiliateReferral { AffiliateId = affiliateId, ReferredUserId = referredUserId, CreatedAt = DateTime.UtcNow };
            _db.AffiliateReferrals.Add(referral);
        }
        referral.SubscriptionId = subscriptionId;
        referral.CommissionAmount = commission;
        referral.Status = AffiliateReferralStatus.Pending;

        aff.PendingBalance += commission;
        aff.TotalEarned += commission;
        await _db.SaveChangesAsync();
    }

    public async Task<AffiliateStats> GetStatsAsync(int affiliateId)
    {
        var aff = await _db.Affiliates.FindAsync(affiliateId);
        return new AffiliateStats
        {
            Clicks = await _db.AffiliateClicks.CountAsync(c => c.AffiliateId == affiliateId),
            Signups = await _db.AffiliateReferrals.CountAsync(r => r.AffiliateId == affiliateId),
            Conversions = await _db.AffiliateReferrals.CountAsync(r => r.AffiliateId == affiliateId && r.CommissionAmount > 0),
            TotalEarned = aff?.TotalEarned ?? 0,
            PendingBalance = aff?.PendingBalance ?? 0,
            PaidBalance = aff?.PaidBalance ?? 0
        };
    }

    public async Task<AffiliatePayoutRequest?> RequestPayoutAsync(int affiliateId, decimal minPayout, string method, string? details)
    {
        var aff = await _db.Affiliates.FindAsync(affiliateId);
        if (aff == null || aff.PendingBalance < minPayout) return null;

        var req = new AffiliatePayoutRequest
        {
            AffiliateId = affiliateId,
            Amount = aff.PendingBalance,
            PaymentMethod = method,
            PaymentDetails = details,
            Status = AffiliatePayoutStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        _db.AffiliatePayoutRequests.Add(req);
        await _db.SaveChangesAsync();
        return req;
    }

    /// <summary>Returns IPs that appear on multiple referral signups (possible self-referral / fraud).</summary>
    public async Task<List<string>> DetectFraudAsync(int affiliateId)
    {
        var referrals = await _db.AffiliateReferrals
            .Where(r => r.AffiliateId == affiliateId && r.SignupIp != null)
            .ToListAsync();
        return referrals.GroupBy(r => r.SignupIp!)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} ({g.Count()} signups)")
            .ToList();
    }

    private async Task<string> UniqueCodeAsync()
    {
        for (var i = 0; i < 10; i++)
        {
            var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToUpperInvariant();
            if (!await _db.Affiliates.AnyAsync(a => a.Code == code)) return code;
        }
        return Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    }
}
