using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Portal;
using SRXPanel.Services.Store;

namespace SRXPanel.Pages.Client;

public class DashboardModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IFileManagerService _fileManager;
    private readonly INotificationService _notifications;
    private readonly ITicketService _tickets;
    private readonly IStoreService _store;

    public DashboardModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        IFileManagerService fileManager, INotificationService notifications, ITicketService tickets,
        IStoreService store)
    {
        _db = db;
        _userManager = userManager;
        _fileManager = fileManager;
        _notifications = notifications;
        _tickets = tickets;
        _store = store;
    }

    public ApplicationUser CurrentUser { get; set; } = null!;
    public Subscription? Subscription { get; set; }
    public Plan? Plan { get; set; }

    public int DomainCount { get; set; }
    public int EmailCount { get; set; }
    public int DatabaseCount { get; set; }
    public int FtpCount { get; set; }
    public long DiskUsedMB { get; set; }
    public long BandwidthUsedMB { get; set; }

    public List<AuditLog> RecentActivity { get; set; } = new();
    public List<Notification> UnreadNotifications { get; set; } = new();
    public int OpenTickets { get; set; }

    // Store widgets
    public List<ServiceView> Services { get; set; } = new();
    public int? RenewalDays { get; set; }              // days until the primary service renews (if within 30)
    public bool SuggestUpgrade { get; set; }           // using >80% of disk
    public int DiskUsagePercent { get; set; }

    public int MaxDomains => Plan?.MaxDomains ?? 0;
    public int MaxEmails => Plan?.MaxEmails ?? 0;
    public int MaxDatabases => Plan?.MaxDatabases ?? 0;
    public int MaxFtp => Plan?.MaxFtpAccounts ?? 0;

    public static int Pct(long used, long max) => max <= 0 ? 0 : (int)Math.Min(100, 100.0 * used / max);
    public static int Pct(int used, int max) => max <= 0 ? 0 : (int)Math.Min(100, 100.0 * used / max);

    public async Task OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return;
        CurrentUser = user;

        Subscription = await _db.Subscriptions.Include(s => s.Plan)
            .Where(s => s.UserId == user.Id)
            .OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync();
        Plan = Subscription?.Plan;

        DomainCount = await _db.Domains.CountAsync(d => d.UserId == user.Id);
        EmailCount = await _db.EmailAccounts.CountAsync(e => e.UserId == user.Id);
        DatabaseCount = await _db.Databases.CountAsync(d => d.UserId == user.Id);
        FtpCount = await _db.FtpAccounts.CountAsync(f => f.UserId == user.Id);
        DiskUsedMB = _fileManager.GetUsedBytes(user.Id) / 1024 / 1024;
        BandwidthUsedMB = 0;

        RecentActivity = await _db.AuditLogs.Where(a => a.UserId == user.Id)
            .OrderByDescending(a => a.Timestamp).Take(10).ToListAsync();
        UnreadNotifications = await _notifications.GetRecentAsync(user.Id, 5);
        OpenTickets = await _tickets.OpenCountAsync(user.Id);

        // Store widgets
        Services = await _store.GetActiveServicesAsync(user.Id);
        if (Subscription != null && Subscription.Status != SubscriptionStatus.Cancelled)
        {
            var days = (int)Math.Ceiling((Subscription.CurrentPeriodEnd - DateTime.UtcNow).TotalDays);
            if (days <= 30) RenewalDays = Math.Max(0, days);
        }
        if (Plan is { DiskQuotaMB: > 0 })
        {
            DiskUsagePercent = Pct(DiskUsedMB, Plan.DiskQuotaMB);
            SuggestUpgrade = DiskUsagePercent >= 80;
        }
    }
}
