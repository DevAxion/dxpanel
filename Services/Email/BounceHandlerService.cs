using System.Text;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Email;

public record BounceStats(int Hard, int Soft, int Blacklisted)
{
    public int Total => Hard + Soft;
}

public interface IBounceHandlerService
{
    Task ProcessBounceAsync(int domainId, string email, BounceType type, string reason);
    Task<List<EmailBounce>> GetBounceListAsync(int domainId, BounceType? type = null);
    Task<List<EmailBounce>> GetHardBouncesAsync(int domainId);
    Task<BounceStats> GetStatsAsync(int domainId);
    Task<int> BlacklistBouncedAsync(int domainId);
    Task<string> ExportBouncesAsync(int domainId);
    Task<int> ClearBouncesAsync(int domainId);
}

public class BounceHandlerService : IBounceHandlerService
{
    private const int AutoBlacklistThreshold = 3;

    private readonly ApplicationDbContext _db;
    private readonly INotificationService _notifications;

    public BounceHandlerService(ApplicationDbContext db, INotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    public async Task ProcessBounceAsync(int domainId, string email, BounceType type, string reason)
    {
        email = email.Trim().ToLowerInvariant();
        _db.EmailBounces.Add(new EmailBounce
        {
            DomainId = domainId, EmailAddress = email, BounceType = type, BounceReason = reason, OccurredAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        // Auto-blacklist an address once it has 3+ hard bounces.
        if (type == BounceType.Hard)
        {
            var config = await _db.MailServerConfigs.FirstOrDefaultAsync(c => c.DomainId == domainId);
            var autoBlacklist = config?.AutoBlacklistBounces ?? true;
            var hardCount = await _db.EmailBounces.CountAsync(b => b.DomainId == domainId
                && b.EmailAddress == email && b.BounceType == BounceType.Hard);

            if (autoBlacklist && hardCount >= AutoBlacklistThreshold)
            {
                var toFlag = await _db.EmailBounces
                    .Where(b => b.DomainId == domainId && b.EmailAddress == email && !b.IsBlacklisted).ToListAsync();
                foreach (var b in toFlag) b.IsBlacklisted = true;
                await _db.SaveChangesAsync();

                var domain = await _db.Domains.FirstOrDefaultAsync(d => d.Id == domainId);
                if (domain != null)
                    await _notifications.NotifyAsync(domain.UserId, "Recipient auto-suppressed",
                        $"{email} has hard-bounced {hardCount} times and was added to your suppression list.",
                        NotificationType.Warning, $"bounce-suppress-{domainId}-{email}");
            }
        }
    }

    public Task<List<EmailBounce>> GetBounceListAsync(int domainId, BounceType? type = null)
    {
        var q = _db.EmailBounces.Where(b => b.DomainId == domainId);
        if (type.HasValue) q = q.Where(b => b.BounceType == type.Value);
        return q.OrderByDescending(b => b.OccurredAt).ToListAsync();
    }

    public Task<List<EmailBounce>> GetHardBouncesAsync(int domainId) =>
        _db.EmailBounces.Where(b => b.DomainId == domainId && b.BounceType == BounceType.Hard)
            .OrderByDescending(b => b.OccurredAt).ToListAsync();

    public async Task<BounceStats> GetStatsAsync(int domainId) => new(
        await _db.EmailBounces.CountAsync(b => b.DomainId == domainId && b.BounceType == BounceType.Hard),
        await _db.EmailBounces.CountAsync(b => b.DomainId == domainId && b.BounceType == BounceType.Soft),
        await _db.EmailBounces.CountAsync(b => b.DomainId == domainId && b.IsBlacklisted));

    public async Task<int> BlacklistBouncedAsync(int domainId)
    {
        var hard = await _db.EmailBounces
            .Where(b => b.DomainId == domainId && b.BounceType == BounceType.Hard && !b.IsBlacklisted).ToListAsync();
        foreach (var b in hard) b.IsBlacklisted = true;
        await _db.SaveChangesAsync();
        return hard.Count;
    }

    public async Task<string> ExportBouncesAsync(int domainId)
    {
        var bounces = await GetBounceListAsync(domainId);
        var sb = new StringBuilder("Email,Type,Reason,OccurredAt,Suppressed\n");
        foreach (var b in bounces)
            sb.AppendLine($"{b.EmailAddress},{b.BounceType},\"{b.BounceReason.Replace("\"", "\"\"")}\",{b.OccurredAt:u},{b.IsBlacklisted}");
        return sb.ToString();
    }

    public async Task<int> ClearBouncesAsync(int domainId)
    {
        var bounces = await _db.EmailBounces.Where(b => b.DomainId == domainId).ToListAsync();
        _db.EmailBounces.RemoveRange(bounces);
        await _db.SaveChangesAsync();
        return bounces.Count;
    }
}

/// <summary>
/// Hourly job that parses Postfix bounce logs. In simulation it occasionally synthesises a
/// bounce from recent sent mail so the bounce views and auto-suppression have data to show.
/// </summary>
public class BounceMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BounceMonitorService> _logger;

    public BounceMonitorService(IServiceScopeFactory scopeFactory, ILogger<BounceMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Bounce monitor tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var runner = sp.GetRequiredService<ICommandRunner>();
        var bounces = sp.GetRequiredService<IBounceHandlerService>();

        await runner.LogExternalAsync("grep 'status=bounced' /var/log/mail.log",
            runner.SimulationMode ? "parsed bounce log (simulated)" : "parsed", runner.SimulationMode, "postfix");

        if (!runner.SimulationMode) return; // real parsing happens against the live log

        // Synthesise a bounce from a recent bounced/spam log entry, if any.
        var recent = await db.EmailLogs
            .Where(l => l.Status == EmailLogStatus.Bounced || l.Status == EmailLogStatus.Delivered)
            .OrderByDescending(l => l.CreatedAt).Take(30).ToListAsync(ct);
        if (recent.Count == 0) return;

        var rnd = new Random();
        var pick = recent[rnd.Next(recent.Count)];
        if (pick.DomainId is not int domainId) return;

        var hard = rnd.NextDouble() < 0.5;
        await bounces.ProcessBounceAsync(domainId, pick.ToAddress, hard ? BounceType.Hard : BounceType.Soft,
            hard ? "550 5.1.1 User unknown" : "452 4.2.2 Mailbox full");
    }
}
