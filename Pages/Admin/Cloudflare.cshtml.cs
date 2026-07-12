using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Pages.Admin;

public class CloudflareModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public CloudflareModel(ApplicationDbContext db) => _db = db;

    public record AccountRow(CloudflareAccount Account, string UserName, int ZoneCount, int ActiveZones,
        long ThreatsToday, long RequestsToday);

    public List<AccountRow> Accounts { get; private set; } = new();

    public int TotalZones { get; private set; }
    public int TotalAccounts { get; private set; }
    public long GlobalThreats { get; private set; }
    public long GlobalRequests { get; private set; }

    public async Task OnGetAsync()
    {
        var accounts = await _db.CloudflareAccounts.Include(a => a.User).ToListAsync();
        var today = DateTime.UtcNow.Date;

        foreach (var account in accounts)
        {
            var zones = await _db.CloudflareDomains.Where(z => z.CloudflareAccountId == account.Id).ToListAsync();
            var zoneIds = zones.Select(z => z.Id).ToList();

            var todayStats = await _db.CloudflareAnalytics
                .Where(a => zoneIds.Contains(a.CloudflareDomainId) && a.Date == today)
                .ToListAsync();

            var threats = todayStats.Sum(s => s.Threats);
            var requests = todayStats.Sum(s => s.Requests);

            Accounts.Add(new AccountRow(account, account.User?.UserName ?? "—",
                zones.Count, zones.Count(z => z.Status == CloudflareZoneStatus.Active), threats, requests));

            GlobalThreats += threats;
            GlobalRequests += requests;
        }

        TotalAccounts = accounts.Count;
        TotalZones = await _db.CloudflareDomains.CountAsync();
    }
}
