using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Services.Reseller;

public class ResellerUsage
{
    public int ClientCount { get; set; }
    public int ActiveClients { get; set; }
    public int SuspendedClients { get; set; }
    public int TrialClients { get; set; }

    public long DiskUsedMB { get; set; }
    public long DiskQuotaMB { get; set; }
    public long BandwidthUsedMB { get; set; }
    public long BandwidthQuotaMB { get; set; }

    public int DomainsUsed { get; set; }
    public int MaxDomains { get; set; }
    public int MaxClients { get; set; }

    public static int Percent(long used, long quota) =>
        quota <= 0 ? 0 : (int)Math.Min(100, Math.Round(100.0 * used / quota));
    public static int Percent(int used, int max) =>
        max <= 0 ? 0 : (int)Math.Min(100, Math.Round(100.0 * used / max));

    public int DiskPercent => Percent(DiskUsedMB, DiskQuotaMB);
    public int BandwidthPercent => Percent(BandwidthUsedMB, BandwidthQuotaMB);
    public int ClientPercent => Percent(ClientCount, MaxClients);
    public int DomainPercent => Percent(DomainsUsed, MaxDomains);
}

public interface IResellerService
{
    Task<ResellerProfile?> GetProfileAsync(string resellerId);
    Task<ResellerProfile?> GetProfileByProfileIdAsync(int profileId);
    Task<ResellerUsage> GetUsageAsync(ResellerProfile profile);
    Task<List<ApplicationUser>> GetClientsAsync(string resellerId);

    /// <summary>Validates a package's limits against the reseller's remaining allocation.</summary>
    Task<(bool Ok, string? Error)> ValidatePackageAsync(ResellerProfile profile, ResellerPackage draft);

    /// <summary>Validates that a reseller can take on one more client with the given disk assignment.</summary>
    Task<(bool Ok, string? Error)> ValidateNewClientAsync(ResellerProfile profile, long diskQuotaMB, int maxDomains);
}

public class ResellerService : IResellerService
{
    private readonly ApplicationDbContext _db;
    private readonly IFileManagerService _fileManager;

    public ResellerService(ApplicationDbContext db, IFileManagerService fileManager)
    {
        _db = db;
        _fileManager = fileManager;
    }

    public Task<ResellerProfile?> GetProfileAsync(string resellerId) =>
        _db.ResellerProfiles.FirstOrDefaultAsync(p => p.UserId == resellerId);

    public Task<ResellerProfile?> GetProfileByProfileIdAsync(int profileId) =>
        _db.ResellerProfiles.Include(p => p.User).FirstOrDefaultAsync(p => p.Id == profileId);

    public async Task<List<ApplicationUser>> GetClientsAsync(string resellerId) =>
        await _db.Users.Include(u => u.ResellerPackage)
            .Where(u => u.ResellerId == resellerId)
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();

    public async Task<ResellerUsage> GetUsageAsync(ResellerProfile profile)
    {
        var clients = await _db.Users.Where(u => u.ResellerId == profile.UserId).ToListAsync();
        var clientIds = clients.Select(c => c.Id).ToList();

        var trialCount = await _db.Subscriptions
            .CountAsync(s => clientIds.Contains(s.UserId) && s.Status == SubscriptionStatus.Trialing);

        var domainsUsed = await _db.Domains.CountAsync(d => clientIds.Contains(d.UserId));

        long diskUsedMB = 0;
        foreach (var c in clients)
        {
            diskUsedMB += _fileManager.GetUsedBytes(c.Id) / 1024 / 1024;
        }

        return new ResellerUsage
        {
            ClientCount = clients.Count,
            ActiveClients = clients.Count(c => c.IsActive),
            SuspendedClients = clients.Count(c => !c.IsActive),
            TrialClients = trialCount,
            DiskUsedMB = diskUsedMB,
            DiskQuotaMB = profile.DiskQuotaMB,
            BandwidthUsedMB = 0,
            BandwidthQuotaMB = profile.BandwidthQuotaMB,
            DomainsUsed = domainsUsed,
            MaxDomains = profile.MaxDomains,
            MaxClients = profile.MaxClients
        };
    }

    public async Task<(bool Ok, string? Error)> ValidatePackageAsync(ResellerProfile profile, ResellerPackage draft)
    {
        // Sum disk already committed to other packages is not enforced (packages are
        // templates); we cap a single package's disk to the reseller's total allocation.
        if (profile.DiskQuotaMB > 0 && draft.DiskQuotaMB > profile.DiskQuotaMB)
            return (false, $"Disk quota cannot exceed your allocation of {profile.DiskQuotaMB} MB.");

        if (profile.MaxDomains > 0 && draft.MaxDomains > profile.MaxDomains)
            return (false, $"Max domains cannot exceed your allocation of {profile.MaxDomains}.");

        if (!profile.AllowEmail && draft.MaxEmails > 0)
            return (false, "Your reseller account is not permitted to offer email hosting.");

        return await Task.FromResult((true, (string?)null));
    }

    public async Task<(bool Ok, string? Error)> ValidateNewClientAsync(ResellerProfile profile, long diskQuotaMB, int maxDomains)
    {
        var usage = await GetUsageAsync(profile);

        if (profile.MaxClients > 0 && usage.ClientCount >= profile.MaxClients)
            return (false, $"You have reached your client limit ({profile.MaxClients}).");

        if (profile.DiskQuotaMB > 0 && usage.DiskUsedMB + diskQuotaMB > profile.DiskQuotaMB)
            return (false, $"Assigning {diskQuotaMB} MB would exceed your remaining disk allocation " +
                           $"({profile.DiskQuotaMB - usage.DiskUsedMB} MB left).");

        if (profile.MaxDomains > 0 && usage.DomainsUsed + maxDomains > profile.MaxDomains)
            return (false, $"Assigning {maxDomains} domains would exceed your domain allocation " +
                           $"({profile.MaxDomains - usage.DomainsUsed} left).");

        return (true, null);
    }
}
