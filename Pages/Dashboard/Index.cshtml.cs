using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Nodes;

namespace SRXPanel.Pages.Dashboard;

public class ResourceUsage
{
    public long DiskUsedMB { get; set; }
    public long DiskQuotaMB { get; set; }
    public long BandwidthUsedMB { get; set; }
    public long BandwidthQuotaMB { get; set; }
    public int Databases { get; set; }
    public int MaxDatabases { get; set; }
    public int Emails { get; set; }
    public int MaxEmails { get; set; }
    public int FtpAccounts { get; set; }
    public int MaxFtpAccounts { get; set; }
    public int DomainsCount { get; set; }
    public int MaxDomains { get; set; }

    public static int Percent(long used, long quota) =>
        quota <= 0 ? 0 : (int)Math.Min(100, Math.Round(100.0 * used / quota));
    public static int Percent(int used, int max) =>
        max <= 0 ? 0 : (int)Math.Min(100, Math.Round(100.0 * used / max));
}

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISystemStatsService _systemStats;
    private readonly IFileManagerService _fileManager;
    private readonly INotificationService _notifications;
    private readonly INodeManagerService _nodes;

    public IndexModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, ISystemStatsService systemStats,
        IFileManagerService fileManager, INotificationService notifications, INodeManagerService nodes)
    {
        _db = db;
        _userManager = userManager;
        _systemStats = systemStats;
        _fileManager = fileManager;
        _notifications = notifications;
        _nodes = nodes;
    }

    public string CurrentRole { get; set; } = string.Empty;

    // SuperAdmin stats
    public int TotalUsers { get; set; }
    public int TotalResellers { get; set; }
    public int TotalClients { get; set; }
    public int TotalDomains { get; set; }
    public SystemStats? SystemStats { get; set; }

    // Fleet (Phase 14) — SuperAdmin only
    public int FleetTotal { get; set; }
    public int FleetOnline { get; set; }
    public List<NodeCapacity> FleetCapacities { get; set; } = new();
    public int OpenAlertsCount { get; set; }
    public List<NodeAlert> RecentAlerts { get; set; } = new();

    public int FleetOnlinePercent => FleetTotal == 0 ? 0 : (int)Math.Round(100.0 * FleetOnline / FleetTotal);
    public double FleetAvgCpu => FleetCapacities.Count == 0 ? 0 : FleetCapacities.Average(c => c.CpuPercent);
    public double FleetAvgRam => FleetCapacities.Count == 0 ? 0 : FleetCapacities.Average(c => c.RamPercent);
    public double FleetAvgDisk => FleetCapacities.Count == 0 ? 0 : FleetCapacities.Average(c => c.DiskPercent);

    // Reseller stats
    public int MyClientsCount { get; set; }
    public int MyClientsDomainsCount { get; set; }

    // Client stats
    public List<Domain> MyDomains { get; set; } = new();

    // Per-user resource usage (all roles, for their own account)
    public ResourceUsage Usage { get; set; } = new();

    // Billing / trial state
    public Subscription? Subscription { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Page();

        // Clients use the dedicated self-service portal dashboard.
        if (User.IsInRole(Roles.Client))
        {
            return RedirectToPage("/Client/Dashboard");
        }

        Subscription = await _db.Subscriptions.Include(s => s.Plan)
            .Where(s => s.UserId == user.Id)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();

        await BuildResourceUsageAsync(user);
        await RunAutoNotificationsAsync(user);

        if (User.IsInRole(Roles.SuperAdmin))
        {
            CurrentRole = Roles.SuperAdmin;
            TotalUsers = await _userManager.Users.CountAsync();
            TotalResellers = (await _userManager.GetUsersInRoleAsync(Roles.Reseller)).Count;
            TotalClients = (await _userManager.GetUsersInRoleAsync(Roles.Client)).Count;
            TotalDomains = await _db.Domains.CountAsync();
            SystemStats = await _systemStats.GetStatsAsync();

            // Fleet status
            var fleetNodes = await _db.ServerNodes.Where(n => n.IsActive).ToListAsync();
            FleetTotal = fleetNodes.Count;
            FleetOnline = fleetNodes.Count(n => n.Status == NodeStatus.Online);
            FleetCapacities = await _nodes.GetFleetCapacityAsync();
            OpenAlertsCount = await _db.NodeAlerts.CountAsync(a => !a.IsAcknowledged);
            RecentAlerts = await _db.NodeAlerts.Include(a => a.Node)
                .OrderByDescending(a => a.CreatedAt)
                .Take(5)
                .ToListAsync();
        }
        else if (User.IsInRole(Roles.Reseller))
        {
            CurrentRole = Roles.Reseller;
            MyClientsCount = await _userManager.Users.CountAsync(u => u.ResellerId == user.Id);
            MyClientsDomainsCount = await _db.Domains.CountAsync(d => d.User != null && d.User.ResellerId == user.Id);
        }
        else
        {
            CurrentRole = Roles.Client;
            MyDomains = await _db.Domains.Where(d => d.UserId == user.Id).ToListAsync();
        }

        return Page();
    }

    private async Task BuildResourceUsageAsync(ApplicationUser user)
    {
        var package = user.PackageId.HasValue ? await _db.Packages.FindAsync(user.PackageId.Value) : null;

        var usedBytes = _fileManager.GetUsedBytes(user.Id);

        Usage = new ResourceUsage
        {
            DiskUsedMB = usedBytes / 1024 / 1024,
            DiskQuotaMB = user.DiskQuotaMB,
            BandwidthUsedMB = 0,
            BandwidthQuotaMB = user.BandwidthQuotaMB,
            Databases = await _db.Databases.CountAsync(d => d.UserId == user.Id),
            MaxDatabases = package?.MaxDatabases ?? 0,
            Emails = await _db.EmailAccounts.CountAsync(e => e.UserId == user.Id),
            MaxEmails = package?.MaxEmails ?? 0,
            FtpAccounts = await _db.FtpAccounts.CountAsync(f => f.UserId == user.Id),
            MaxFtpAccounts = package?.MaxFtpAccounts ?? 0,
            DomainsCount = await _db.Domains.CountAsync(d => d.UserId == user.Id),
            MaxDomains = package?.MaxDomains ?? 0
        };
    }

    private async Task RunAutoNotificationsAsync(ApplicationUser user)
    {
        // Disk usage > 80%
        if (Usage.DiskQuotaMB > 0)
        {
            var pct = ResourceUsage.Percent(Usage.DiskUsedMB, Usage.DiskQuotaMB);
            if (pct >= 80)
            {
                await _notifications.NotifyAsync(user.Id, "Disk usage high",
                    $"Your disk usage is at {pct}% of your {Usage.DiskQuotaMB} MB quota.",
                    NotificationType.Warning, dedupeKey: "disk-high");
            }
        }

        // SSL certificates expiring within 30 days
        var soon = DateTime.UtcNow.AddDays(30);
        var expiring = await _db.SslCertificates
            .Include(c => c.Domain)
            .Where(c => c.UserId == user.Id && c.ExpiresAt <= soon)
            .ToListAsync();

        foreach (var cert in expiring)
        {
            var days = (int)(cert.ExpiresAt.Date - DateTime.UtcNow.Date).TotalDays;
            var msg = days < 0
                ? $"The SSL certificate for {cert.Domain?.DomainName} has expired."
                : $"The SSL certificate for {cert.Domain?.DomainName} expires in {days} day(s).";
            await _notifications.NotifyAsync(user.Id, "SSL certificate expiring", msg,
                NotificationType.Warning, dedupeKey: $"ssl-expiry-{cert.Id}");
        }
    }
}
