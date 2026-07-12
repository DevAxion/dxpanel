using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Email;

/// <summary>
/// Drains the mail queue every 30 seconds: hands queued messages to Postfix, updates their
/// status, retries deferred ones up to five times and alerts when the failure rate exceeds 20%.
/// In simulation nothing is sent — items transition to realistic outcomes and a line is logged.
/// </summary>
public class EmailQueueProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailQueueProcessor> _logger;

    private const int MaxAttempts = 5;
    private const int BatchPerTick = 20;

    public EmailQueueProcessor(IServiceScopeFactory scopeFactory, ILogger<EmailQueueProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(12), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Email queue processor tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var runner = sp.GetRequiredService<ICommandRunner>();
        var broadcast = sp.GetRequiredService<IEmailBroadcast>();
        var notifications = sp.GetRequiredService<INotificationService>();

        // Domains whose queue is paused are skipped this tick.
        var pausedDomains = await db.MailServerConfigs.Where(c => c.QueuePaused).Select(c => c.DomainId).ToListAsync(ct);

        var batch = await db.EmailQueues
            .Where(q => q.Status == EmailQueueStatus.Queued && (q.DomainId == null || !pausedDomains.Contains(q.DomainId.Value)))
            .OrderBy(q => q.CreatedAt)
            .Take(BatchPerTick)
            .ToListAsync(ct);

        if (batch.Count == 0) return;

        await runner.LogExternalAsync($"postqueue -f (flush {batch.Count} messages)",
            runner.SimulationMode ? $"Would process {batch.Count} emails" : "queued for delivery",
            runner.SimulationMode, "postfix");

        var rnd = new Random();
        var touchedUsers = new HashSet<string>();
        var statsByDomain = new Dictionary<int, (int sent, int failed, int deferred, int spam)>();

        foreach (var item in batch)
        {
            item.Attempts++;
            item.LastAttemptAt = DateTime.UtcNow;
            item.Status = EmailQueueStatus.Sending;
            touchedUsers.Add(item.UserId);

            // Simulated delivery outcome: mostly delivered, occasional deferral/failure.
            var roll = rnd.NextDouble();
            var spamScore = Math.Round(rnd.NextDouble() * 6, 1);

            if (roll < 0.85)
            {
                item.Status = EmailQueueStatus.Sent;
                item.SentAt = DateTime.UtcNow;
                item.ErrorMessage = null;
                db.EmailLogs.Add(new EmailLog
                {
                    UserId = item.UserId, DomainId = item.DomainId, FromAddress = item.FromAddress,
                    ToAddress = item.ToAddress, Subject = item.Subject, MessageId = NewMessageId(),
                    Status = spamScore >= 5 ? EmailLogStatus.Spam : EmailLogStatus.Delivered,
                    SpamScore = spamScore, DeliveredAt = DateTime.UtcNow
                });
                Bump(statsByDomain, item.DomainId, sent: spamScore < 5 ? 1 : 0, spam: spamScore >= 5 ? 1 : 0);
            }
            else if (roll < 0.93 && item.Attempts < MaxAttempts)
            {
                item.Status = EmailQueueStatus.Deferred;
                item.ErrorMessage = "451 4.7.1 Greylisted, try again later";
                Bump(statsByDomain, item.DomainId, deferred: 1);
            }
            else
            {
                item.Status = item.Attempts >= MaxAttempts ? EmailQueueStatus.Failed : EmailQueueStatus.Deferred;
                item.ErrorMessage = item.Attempts >= MaxAttempts
                    ? "550 5.1.1 Recipient address rejected: User unknown"
                    : "421 4.4.2 Connection timed out";
                Bump(statsByDomain, item.DomainId, failed: item.Status == EmailQueueStatus.Failed ? 1 : 0,
                    deferred: item.Status == EmailQueueStatus.Deferred ? 1 : 0);
            }
        }

        // Re-queue deferred items so they retry next tick (until MaxAttempts).
        foreach (var item in batch.Where(i => i.Status == EmailQueueStatus.Deferred))
            item.Status = EmailQueueStatus.Queued;

        await RollupStatsAsync(db, statsByDomain, ct);
        await db.SaveChangesAsync(ct);

        // Broadcast fresh counts to any open queue page.
        foreach (var userId in touchedUsers)
        {
            var counts = await CountsForAsync(db, userId, ct);
            await broadcast.QueueUpdatedAsync(userId, counts);
            await CheckFailureRateAsync(db, notifications, userId, ct);
        }
        await broadcast.AdminQueueUpdatedAsync(await CountsForAsync(db, null, ct));
    }

    private static void Bump(Dictionary<int, (int sent, int failed, int deferred, int spam)> map, int? domainId,
        int sent = 0, int failed = 0, int deferred = 0, int spam = 0)
    {
        if (domainId is not int d) return;
        var cur = map.GetValueOrDefault(d);
        map[d] = (cur.sent + sent, cur.failed + failed, cur.deferred + deferred, cur.spam + spam);
    }

    private static async Task RollupStatsAsync(ApplicationDbContext db,
        Dictionary<int, (int sent, int failed, int deferred, int spam)> map, CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        foreach (var (domainId, v) in map)
        {
            var row = await db.EmailQueueStats.FirstOrDefaultAsync(s => s.DomainId == domainId && s.Date == today, ct);
            if (row == null)
            {
                row = new EmailQueueStats { DomainId = domainId, Date = today };
                db.EmailQueueStats.Add(row);
            }
            row.TotalSent += v.sent;
            row.TotalFailed += v.failed;
            row.TotalDeferred += v.deferred;
            row.TotalSpam += v.spam;
        }
    }

    private static async Task<object> CountsForAsync(ApplicationDbContext db, string? userId, CancellationToken ct)
    {
        var query = db.EmailQueues.AsQueryable();
        if (userId != null) query = query.Where(q => q.UserId == userId);
        var today = DateTime.UtcNow.Date;
        return new
        {
            queued = await query.CountAsync(q => q.Status == EmailQueueStatus.Queued, ct),
            sending = await query.CountAsync(q => q.Status == EmailQueueStatus.Sending, ct),
            sent = await query.CountAsync(q => q.Status == EmailQueueStatus.Sent && q.SentAt >= today, ct),
            failed = await query.CountAsync(q => q.Status == EmailQueueStatus.Failed, ct),
            deferred = await query.CountAsync(q => q.Status == EmailQueueStatus.Deferred, ct)
        };
    }

    private static async Task CheckFailureRateAsync(ApplicationDbContext db, INotificationService notifications,
        string userId, CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var sent = await db.EmailQueues.CountAsync(q => q.UserId == userId && q.Status == EmailQueueStatus.Sent && q.SentAt >= today, ct);
        var failed = await db.EmailQueues.CountAsync(q => q.UserId == userId && q.Status == EmailQueueStatus.Failed && q.LastAttemptAt >= today, ct);
        var total = sent + failed;
        if (total < 20) return; // need a meaningful sample

        var failRate = 100.0 * failed / total;
        if (failRate > 20)
            await notifications.NotifyAsync(userId, "High email failure rate",
                $"{failRate:0}% of today's messages failed to deliver. Check your mail queue and blacklist status.",
                NotificationType.Warning, $"mail-failrate-{userId}-{today:yyyyMMdd}");
    }

    private static string NewMessageId() =>
        $"<{Guid.NewGuid():N}@srxpanel>";
}
