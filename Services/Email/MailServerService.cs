using System.Text;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Email;

public record PostfixStatus(bool Running, int QueueSize, int Processes, int ActiveDelivery);
public record DovecotStatus(bool Running, int Connections, int MemoryMB);
public record PostfixQueueItem(string QueueId, string Sender, string Recipient, int SizeKB, string Arrival, string Status);

public interface IMailServerService
{
    Task<MailServerConfig> GetConfigAsync(int domainId);
    Task UpdateConfigAsync(int domainId, Action<MailServerConfig> apply);

    Task<PostfixStatus> GetPostfixStatusAsync();
    Task<DovecotStatus> GetDovecotStatusAsync();

    Task<ServiceResult> ReloadPostfixAsync();
    Task<ServiceResult> ReloadDovecotAsync();
    Task<ServiceResult> FlushPostfixQueueAsync();

    Task<List<PostfixQueueItem>> GetPostfixQueueAsync();
    Task<ServiceResult> DeleteFromPostfixQueueAsync(string queueId);

    Task<string> GetMailLogsAsync(int lines = 100);
}

public class MailServerService : IMailServerService
{
    private const string ServiceName = "postfix";

    private readonly ApplicationDbContext _db;
    private readonly ICommandRunner _runner;

    public MailServerService(ApplicationDbContext db, ICommandRunner runner)
    {
        _db = db;
        _runner = runner;
    }

    private bool Sim => _runner.SimulationMode;

    public async Task<MailServerConfig> GetConfigAsync(int domainId)
    {
        var config = await _db.MailServerConfigs.Include(c => c.Domain).FirstOrDefaultAsync(c => c.DomainId == domainId);
        if (config == null)
        {
            var domain = await _db.Domains.FirstOrDefaultAsync(d => d.Id == domainId);
            var host = domain != null ? $"mail.{domain.DomainName}" : "mail.example.com";
            config = new MailServerConfig { DomainId = domainId, SmtpHost = host, ImapHost = host, Pop3Host = host };
            _db.MailServerConfigs.Add(config);
            await _db.SaveChangesAsync();
        }
        return config;
    }

    public async Task UpdateConfigAsync(int domainId, Action<MailServerConfig> apply)
    {
        var config = await GetConfigAsync(domainId);
        apply(config);
        await _db.SaveChangesAsync();
        await _runner.LogExternalAsync($"postmap mail config for domain {domainId}", "config written + reloaded", Sim, ServiceName);
    }

    public async Task<PostfixStatus> GetPostfixStatusAsync()
    {
        if (Sim)
        {
            await _runner.LogExternalAsync("systemctl status postfix; postqueue -p | tail -1", "running", true, ServiceName);
            var rnd = new Random();
            var queued = await _db.EmailQueues.CountAsync(q => q.Status == EmailQueueStatus.Queued || q.Status == EmailQueueStatus.Deferred);
            var size = queued > 0 ? queued : rnd.Next(5, 50);
            return new PostfixStatus(true, size, rnd.Next(2, 8), rnd.Next(0, 5));
        }

        var result = await _runner.RunAsync("systemctl is-active postfix && postqueue -p | grep -c '^[A-F0-9]'", ServiceName);
        int.TryParse(result.Output.Split('\n').LastOrDefault()?.Trim(), out var qsize);
        return new PostfixStatus(result.Success, qsize, 0, 0);
    }

    public async Task<DovecotStatus> GetDovecotStatusAsync()
    {
        if (Sim)
        {
            await _runner.LogExternalAsync("doveadm who | wc -l", "connections counted", true, "dovecot");
            var rnd = new Random();
            return new DovecotStatus(true, rnd.Next(3, 60), rnd.Next(40, 180));
        }
        var result = await _runner.RunAsync("systemctl is-active dovecot && doveadm who | wc -l", "dovecot");
        int.TryParse(result.Output.Split('\n').LastOrDefault()?.Trim(), out var conns);
        return new DovecotStatus(result.Success, conns, 0);
    }

    public Task<ServiceResult> ReloadPostfixAsync() => ReloadAsync("postfix");
    public Task<ServiceResult> ReloadDovecotAsync() => ReloadAsync("dovecot");

    private async Task<ServiceResult> ReloadAsync(string unit)
    {
        var result = await _runner.RunAsync($"systemctl reload {unit}", unit);
        return result.Success ? ServiceResult.Ok($"{unit} reloaded{(Sim ? " (simulated)" : "")}.", result)
                              : ServiceResult.Fail($"Failed to reload {unit}.", result);
    }

    public async Task<ServiceResult> FlushPostfixQueueAsync()
    {
        var result = await _runner.RunAsync("postqueue -f", ServiceName);
        return ServiceResult.Ok($"Postfix queue flushed{(Sim ? " (simulated)" : "")}.", result);
    }

    public async Task<List<PostfixQueueItem>> GetPostfixQueueAsync()
    {
        if (Sim)
        {
            await _runner.LogExternalAsync("postqueue -p", "queue listed", true, ServiceName);
            var queued = await _db.EmailQueues.Include(q => q.Domain)
                .Where(q => q.Status == EmailQueueStatus.Queued || q.Status == EmailQueueStatus.Deferred)
                .OrderBy(q => q.CreatedAt).Take(25).ToListAsync();
            var rnd = new Random(1);
            return queued.Select(q => new PostfixQueueItem(
                $"{rnd.Next(0x100000, 0xFFFFFF):X6}", q.FromAddress, q.ToAddress, rnd.Next(2, 90),
                q.CreatedAt.ToString("MMM dd HH:mm:ss"),
                q.Status == EmailQueueStatus.Deferred ? "deferred" : "queued")).ToList();
        }

        var result = await _runner.RunAsync("postqueue -p", ServiceName);
        return ParsePostfixQueue(result.Output);
    }

    public async Task<ServiceResult> DeleteFromPostfixQueueAsync(string queueId)
    {
        var safe = new string(queueId.Where(char.IsLetterOrDigit).ToArray());
        var result = await _runner.RunAsync($"postsuper -d {safe}", ServiceName);
        return ServiceResult.Ok($"Message {safe} removed from the queue{(Sim ? " (simulated)" : "")}.", result);
    }

    public async Task<string> GetMailLogsAsync(int lines = 100)
    {
        if (Sim)
        {
            await _runner.LogExternalAsync($"tail -n {lines} /var/log/mail.log", "log tailed", true, ServiceName);
            return SampleMailLog(lines);
        }
        var result = await _runner.RunAsync($"tail -n {lines} /var/log/mail.log", ServiceName);
        return result.Output;
    }

    // ---------------- helpers ----------------

    private static string SampleMailLog(int lines)
    {
        var now = DateTime.UtcNow;
        string[] templates =
        {
            "postfix/smtp[{pid}]: {qid}: to=<{to}>, relay=mx.example.com[93.184.216.34]:25, delay=1.2, status=sent (250 2.0.0 OK)",
            "postfix/smtpd[{pid}]: connect from mail-out.example.net[203.0.113.9]",
            "postfix/qmgr[{pid}]: {qid}: from=<noreply@srxpanel.net>, size=4213, nrcpt=1 (queue active)",
            "postfix/smtp[{pid}]: {qid}: to=<user@dead.example>, status=bounced (550 5.1.1 User unknown)",
            "dovecot: imap-login: Login: user=<mailbox@example.com>, method=PLAIN, rip=198.51.100.7, secured",
            "postfix/smtp[{pid}]: {qid}: to=<slow@example.org>, status=deferred (connection timed out)"
        };
        var rnd = new Random();
        var sb = new StringBuilder();
        for (var i = lines; i > 0; i--)
        {
            var t = templates[rnd.Next(templates.Length)]
                .Replace("{pid}", rnd.Next(1000, 30000).ToString())
                .Replace("{qid}", $"{rnd.Next(0x100000, 0xFFFFFF):X6}")
                .Replace("{to}", $"user{rnd.Next(1, 999)}@example.com");
            sb.AppendLine($"{now.AddSeconds(-i * 4):MMM dd HH:mm:ss} mail {t}");
        }
        return sb.ToString();
    }

    private static List<PostfixQueueItem> ParsePostfixQueue(string output)
    {
        var items = new List<PostfixQueueItem>();
        foreach (var line in output.Split('\n'))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4 && (char.IsLetterOrDigit(parts[0][0]) && parts[0].Length >= 6))
                items.Add(new PostfixQueueItem(parts[0].TrimEnd('*', '!'), parts.Length > 3 ? parts[^1] : "", "", 0, string.Join(' ', parts.Skip(2).Take(3)), "queued"));
        }
        return items;
    }
}
