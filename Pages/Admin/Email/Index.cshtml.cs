using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Email;

namespace SRXPanel.Pages.Admin.Email;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IEmailQueueService _queue;

    public IndexModel(ApplicationDbContext db, IEmailQueueService queue)
    {
        _db = db;
        _queue = queue;
    }

    public int SentToday { get; private set; }
    public int SentWeek { get; private set; }
    public int SentMonth { get; private set; }
    public int TotalFailed { get; private set; }
    public int TotalDeferred { get; private set; }
    public double DeliveryRate { get; private set; }
    public QueueCounts Queue { get; private set; } = new(0, 0, 0, 0, 0, 0);

    public List<(string domain, int sent)> TopDomains { get; private set; } = new();
    public List<(DateTime date, int sent, int failed)> Chart { get; private set; } = new();
    public List<BlacklistEntry> Listed { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var now = DateTime.UtcNow;
        var today = now.Date;

        SentToday = await _db.EmailLogs.CountAsync(l => l.Status == EmailLogStatus.Delivered && l.CreatedAt >= today);
        SentWeek = await _db.EmailLogs.CountAsync(l => l.Status == EmailLogStatus.Delivered && l.CreatedAt >= today.AddDays(-7));
        SentMonth = await _db.EmailLogs.CountAsync(l => l.Status == EmailLogStatus.Delivered && l.CreatedAt >= today.AddDays(-30));
        TotalFailed = await _db.EmailQueues.CountAsync(q => q.Status == EmailQueueStatus.Failed);
        TotalDeferred = await _db.EmailQueues.CountAsync(q => q.Status == EmailQueueStatus.Deferred);
        Queue = await _queue.GetQueueSizeAsync();
        DeliveryRate = await _queue.GetDeliveryRateAsync();

        // Top sending domains (last 30 days).
        var topRaw = await _db.EmailLogs
            .Where(l => l.CreatedAt >= today.AddDays(-30) && l.DomainId != null)
            .GroupBy(l => l.Domain!.DomainName)
            .Select(g => new { Domain = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).Take(8)
            .ToListAsync();
        TopDomains = topRaw.Select(x => (x.Domain, x.Count)).ToList();

        // 14-day delivered vs failed chart from daily stats.
        var stats = await _db.EmailQueueStats.Where(s => s.Date >= today.AddDays(-14))
            .GroupBy(s => s.Date)
            .Select(g => new { Date = g.Key, Sent = g.Sum(x => x.TotalSent), Failed = g.Sum(x => x.TotalFailed) })
            .OrderBy(x => x.Date).ToListAsync();
        Chart = stats.Select(x => (x.Date, x.Sent, x.Failed)).ToList();

        Listed = await _db.BlacklistEntries.Include(e => e.Domain)
            .Where(e => e.IsListed && !e.IsResolved)
            .OrderByDescending(e => e.FirstDetectedAt).Take(20).ToListAsync();
    }
}
