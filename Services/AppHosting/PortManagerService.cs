using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Services.AppHosting;

/// <summary>
/// Hands out and tracks TCP ports for hosted apps within a configurable range (default 3000-9999).
/// Ports are recorded via the owning HostedApp row, so allocation just finds the lowest free port.
/// </summary>
public interface IPortManagerService
{
    int RangeStart { get; }
    int RangeEnd { get; }
    int PerUserLimit { get; }

    Task<int> AllocatePortAsync(string userId);
    Task<bool> IsPortInUseAsync(int port);
    Task<List<int>> GetUserPortsAsync(string userId);
    Task<Dictionary<int, string>> GetPlatformPortsAsync();
    Task<bool> UserAtLimitAsync(string userId);
}

public class PortManagerService : IPortManagerService
{
    private readonly ApplicationDbContext _db;

    public int RangeStart => 3000;
    public int RangeEnd => 9999;
    public int PerUserLimit => 10;

    public PortManagerService(ApplicationDbContext db) => _db = db;

    public async Task<int> AllocatePortAsync(string userId)
    {
        var used = await _db.HostedApps
            .Where(a => a.Status != HostedAppStatus.Stopped || a.Port > 0)
            .Select(a => a.Port).ToHashSetAsync();

        for (var port = RangeStart; port <= RangeEnd; port++)
            if (!used.Contains(port))
                return port;

        throw new InvalidOperationException("No free ports available in the configured range.");
    }

    public Task<bool> IsPortInUseAsync(int port) => _db.HostedApps.AnyAsync(a => a.Port == port);

    public Task<List<int>> GetUserPortsAsync(string userId) =>
        _db.HostedApps.Where(a => a.UserId == userId).Select(a => a.Port).ToListAsync();

    public async Task<Dictionary<int, string>> GetPlatformPortsAsync() =>
        await _db.HostedApps.OrderBy(a => a.Port).ToDictionaryAsync(a => a.Port, a => a.Name);

    public async Task<bool> UserAtLimitAsync(string userId) =>
        await _db.HostedApps.CountAsync(a => a.UserId == userId) >= PerUserLimit;
}
