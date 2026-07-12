using System.Text;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Services.Email;

public record PagedLogs(List<EmailLog> Items, int Page, int TotalPages, int TotalItems);

public record DeliveryReport(int Total, int Delivered, int Bounced, int Deferred, int Spam, int Rejected)
{
    public double DeliveryRate => Total <= 0 ? 0 : Math.Round(100.0 * Delivered / Total, 1);
}

public record LogDetail(EmailLog Log, string Headers, string BodyPreview);

public interface IEmailLogService
{
    Task<PagedLogs> GetLogsAsync(string? userId, int? domainId, DateTime? from, DateTime? to,
        EmailLogStatus? status, int page = 1, int pageSize = 25);
    Task<LogDetail?> GetLogDetailsAsync(string messageId, string? userId = null);
    Task<DeliveryReport> GetDeliveryReportAsync(string? userId, int? domainId, DateTime from, DateTime to);
    Task<List<EmailLog>> SearchLogsAsync(string? userId, string query, int limit = 50);
    Task<string> ExportLogsAsync(string? userId, int? domainId, DateTime from, DateTime to);
}

public class EmailLogService : IEmailLogService
{
    private readonly ApplicationDbContext _db;

    public EmailLogService(ApplicationDbContext db) => _db = db;

    private IQueryable<EmailLog> Base(string? userId, int? domainId, DateTime? from, DateTime? to, EmailLogStatus? status)
    {
        var q = _db.EmailLogs.Include(l => l.Domain).AsQueryable();
        if (userId != null) q = q.Where(l => l.UserId == userId);
        if (domainId.HasValue) q = q.Where(l => l.DomainId == domainId.Value);
        if (from.HasValue) q = q.Where(l => l.CreatedAt >= from.Value);
        if (to.HasValue) q = q.Where(l => l.CreatedAt <= to.Value);
        if (status.HasValue) q = q.Where(l => l.Status == status.Value);
        return q;
    }

    public async Task<PagedLogs> GetLogsAsync(string? userId, int? domainId, DateTime? from, DateTime? to,
        EmailLogStatus? status, int page = 1, int pageSize = 25)
    {
        var q = Base(userId, domainId, from, to, status);
        var total = await q.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        var items = await q.OrderByDescending(l => l.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new PagedLogs(items, page, totalPages, total);
    }

    public async Task<LogDetail?> GetLogDetailsAsync(string messageId, string? userId = null)
    {
        var log = await _db.EmailLogs.Include(l => l.Domain)
            .FirstOrDefaultAsync(l => l.MessageId == messageId && (userId == null || l.UserId == userId));
        if (log == null) return null;

        // Reconstructed headers — a real deployment reads these from the mail log / message store.
        var headers = new StringBuilder()
            .AppendLine($"Message-ID: {log.MessageId}")
            .AppendLine($"Date: {log.CreatedAt:R}")
            .AppendLine($"From: {log.FromAddress}")
            .AppendLine($"To: {log.ToAddress}")
            .AppendLine($"Subject: {log.Subject}")
            .AppendLine($"X-Spam-Score: {log.SpamScore:0.0}")
            .AppendLine($"X-Spam-Status: {(log.SpamScore >= 5 ? "Yes" : "No")}, score={log.SpamScore:0.0} required=5.0")
            .AppendLine($"Delivery-Status: {log.Status}")
            .AppendLine(log.DeliveredAt.HasValue ? $"Delivered: {log.DeliveredAt:R}" : "Delivered: —")
            .AppendLine("Received: from srxpanel (localhost [127.0.0.1]) by mx; via ESMTP")
            .ToString();

        var preview = log.Status switch
        {
            EmailLogStatus.Bounced => "This message bounced. The remote server rejected the recipient address.",
            EmailLogStatus.Spam => "Flagged as spam by SpamAssassin and filed to the Junk folder.",
            EmailLogStatus.Deferred => "Delivery was deferred and will be retried.",
            EmailLogStatus.Rejected => "Rejected at SMTP time by the recipient server.",
            _ => "Message delivered successfully to the recipient's mailbox."
        };

        return new LogDetail(log, headers, preview);
    }

    public async Task<DeliveryReport> GetDeliveryReportAsync(string? userId, int? domainId, DateTime from, DateTime to)
    {
        var q = Base(userId, domainId, from, to, null);
        return new DeliveryReport(
            await q.CountAsync(),
            await q.CountAsync(l => l.Status == EmailLogStatus.Delivered),
            await q.CountAsync(l => l.Status == EmailLogStatus.Bounced),
            await q.CountAsync(l => l.Status == EmailLogStatus.Deferred),
            await q.CountAsync(l => l.Status == EmailLogStatus.Spam),
            await q.CountAsync(l => l.Status == EmailLogStatus.Rejected));
    }

    public Task<List<EmailLog>> SearchLogsAsync(string? userId, string query, int limit = 50)
    {
        query = (query ?? "").Trim();
        var q = _db.EmailLogs.Include(l => l.Domain).AsQueryable();
        if (userId != null) q = q.Where(l => l.UserId == userId);
        if (!string.IsNullOrEmpty(query))
            q = q.Where(l => l.ToAddress.Contains(query) || l.FromAddress.Contains(query)
                || l.Subject.Contains(query) || l.MessageId.Contains(query));
        return q.OrderByDescending(l => l.CreatedAt).Take(limit).ToListAsync();
    }

    public async Task<string> ExportLogsAsync(string? userId, int? domainId, DateTime from, DateTime to)
    {
        var logs = await Base(userId, domainId, from, to, null).OrderByDescending(l => l.CreatedAt).ToListAsync();
        var sb = new StringBuilder("Date,From,To,Subject,Status,SpamScore,MessageId\n");
        foreach (var l in logs)
            sb.AppendLine($"{l.CreatedAt:u},{Csv(l.FromAddress)},{Csv(l.ToAddress)},{Csv(l.Subject)},{l.Status},{l.SpamScore:0.0},{Csv(l.MessageId)}");
        return sb.ToString();
    }

    private static string Csv(string s) => s.Contains(',') || s.Contains('"')
        ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
