using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Email;

public record QueueCounts(int Queued, int Sending, int Sent, int Failed, int Deferred, int SentToday)
{
    public int Active => Queued + Sending + Deferred;
    public int Total => Queued + Sending + Sent + Failed + Deferred;
}

public record PagedQueue(List<EmailQueue> Items, int Page, int TotalPages, int TotalItems);

public interface IEmailQueueService
{
    Task<PagedQueue> GetQueueAsync(string? userId, EmailQueueStatus? status, int page = 1, int pageSize = 25);
    Task<QueueCounts> GetQueueSizeAsync(string? userId = null, int? domainId = null);
    Task<double> GetDeliveryRateAsync(string? userId = null, int? domainId = null);
    Task<List<EmailQueueStats>> GetQueueStatsAsync(int? domainId, DateTime from, DateTime to);

    Task<bool> RetryFailedAsync(int queueId, string? userId = null);
    Task<int> RetryAllFailedAsync(string? userId = null, int? domainId = null);
    Task<bool> DeleteQueuedAsync(int queueId, string? userId = null);
    Task<int> FlushQueueAsync(string? userId = null, int? domainId = null);

    Task PauseQueueAsync(int domainId);
    Task ResumeQueueAsync(int domainId);
    Task<bool> IsPausedAsync(int domainId);
}

public class EmailQueueService : IEmailQueueService
{
    private readonly ApplicationDbContext _db;
    private readonly ICommandRunner _runner;

    public EmailQueueService(ApplicationDbContext db, ICommandRunner runner)
    {
        _db = db;
        _runner = runner;
    }

    public async Task<PagedQueue> GetQueueAsync(string? userId, EmailQueueStatus? status, int page = 1, int pageSize = 25)
    {
        var query = _db.EmailQueues.Include(q => q.Domain).AsQueryable();
        if (userId != null) query = query.Where(q => q.UserId == userId);
        if (status.HasValue) query = query.Where(q => q.Status == status.Value);

        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);

        var items = await query.OrderByDescending(q => q.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new PagedQueue(items, page, totalPages, total);
    }

    public async Task<QueueCounts> GetQueueSizeAsync(string? userId = null, int? domainId = null)
    {
        var query = _db.EmailQueues.AsQueryable();
        if (userId != null) query = query.Where(q => q.UserId == userId);
        if (domainId.HasValue) query = query.Where(q => q.DomainId == domainId.Value);

        var today = DateTime.UtcNow.Date;
        return new QueueCounts(
            await query.CountAsync(q => q.Status == EmailQueueStatus.Queued),
            await query.CountAsync(q => q.Status == EmailQueueStatus.Sending),
            await query.CountAsync(q => q.Status == EmailQueueStatus.Sent),
            await query.CountAsync(q => q.Status == EmailQueueStatus.Failed),
            await query.CountAsync(q => q.Status == EmailQueueStatus.Deferred),
            await query.CountAsync(q => q.Status == EmailQueueStatus.Sent && q.SentAt >= today));
    }

    public async Task<double> GetDeliveryRateAsync(string? userId = null, int? domainId = null)
    {
        var counts = await GetQueueSizeAsync(userId, domainId);
        var attempted = counts.Sent + counts.Failed;
        return attempted <= 0 ? 100 : Math.Round(100.0 * counts.Sent / attempted, 1);
    }

    public Task<List<EmailQueueStats>> GetQueueStatsAsync(int? domainId, DateTime from, DateTime to)
    {
        var query = _db.EmailQueueStats.Where(s => s.Date >= from.Date && s.Date <= to.Date);
        if (domainId.HasValue) query = query.Where(s => s.DomainId == domainId.Value);
        return query.OrderBy(s => s.Date).ToListAsync();
    }

    public async Task<bool> RetryFailedAsync(int queueId, string? userId = null)
    {
        var item = await _db.EmailQueues.FirstOrDefaultAsync(q => q.Id == queueId && (userId == null || q.UserId == userId));
        if (item == null || item.Status is not (EmailQueueStatus.Failed or EmailQueueStatus.Deferred)) return false;
        item.Status = EmailQueueStatus.Queued;
        item.ErrorMessage = null;
        await _db.SaveChangesAsync();
        await _runner.LogExternalAsync($"postqueue -i {queueId} (requeue)", "requeued", _runner.SimulationMode, "postfix");
        return true;
    }

    public async Task<int> RetryAllFailedAsync(string? userId = null, int? domainId = null)
    {
        var query = _db.EmailQueues.Where(q => q.Status == EmailQueueStatus.Failed || q.Status == EmailQueueStatus.Deferred);
        if (userId != null) query = query.Where(q => q.UserId == userId);
        if (domainId.HasValue) query = query.Where(q => q.DomainId == domainId.Value);

        var items = await query.ToListAsync();
        foreach (var item in items) { item.Status = EmailQueueStatus.Queued; item.ErrorMessage = null; }
        await _db.SaveChangesAsync();
        await _runner.LogExternalAsync("postsuper -r ALL deferred", $"{items.Count} requeued", _runner.SimulationMode, "postfix");
        return items.Count;
    }

    public async Task<bool> DeleteQueuedAsync(int queueId, string? userId = null)
    {
        var item = await _db.EmailQueues.FirstOrDefaultAsync(q => q.Id == queueId && (userId == null || q.UserId == userId));
        if (item == null) return false;
        _db.EmailQueues.Remove(item);
        await _db.SaveChangesAsync();
        await _runner.LogExternalAsync($"postsuper -d {queueId}", "deleted", _runner.SimulationMode, "postfix");
        return true;
    }

    public async Task<int> FlushQueueAsync(string? userId = null, int? domainId = null)
    {
        var query = _db.EmailQueues.Where(q => q.Status == EmailQueueStatus.Queued || q.Status == EmailQueueStatus.Deferred);
        if (userId != null) query = query.Where(q => q.UserId == userId);
        if (domainId.HasValue) query = query.Where(q => q.DomainId == domainId.Value);

        var items = await query.ToListAsync();
        _db.EmailQueues.RemoveRange(items);
        await _db.SaveChangesAsync();
        await _runner.LogExternalAsync("postsuper -d ALL", $"{items.Count} flushed", _runner.SimulationMode, "postfix");
        return items.Count;
    }

    public async Task PauseQueueAsync(int domainId)
    {
        var config = await GetOrCreateConfigAsync(domainId);
        config.QueuePaused = true;
        await _db.SaveChangesAsync();
    }

    public async Task ResumeQueueAsync(int domainId)
    {
        var config = await GetOrCreateConfigAsync(domainId);
        config.QueuePaused = false;
        await _db.SaveChangesAsync();
    }

    public async Task<bool> IsPausedAsync(int domainId) =>
        await _db.MailServerConfigs.Where(c => c.DomainId == domainId).Select(c => c.QueuePaused).FirstOrDefaultAsync();

    private async Task<MailServerConfig> GetOrCreateConfigAsync(int domainId)
    {
        var config = await _db.MailServerConfigs.FirstOrDefaultAsync(c => c.DomainId == domainId);
        if (config == null)
        {
            config = new MailServerConfig { DomainId = domainId };
            _db.MailServerConfigs.Add(config);
            await _db.SaveChangesAsync();
        }
        return config;
    }
}
