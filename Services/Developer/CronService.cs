using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Billing;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Developer;

/// <summary>Outcome of validating a raw cron expression.</summary>
public record CronValidation(bool IsValid, string Description, string? Error, DateTime? NextRun);

public interface ICronService
{
    Task<List<CronJob>> GetJobsAsync(string userId);
    Task<CronJob?> GetJobAsync(string userId, int id);

    Task<CronJob> CreateJobAsync(string userId, string command, string schedule, string? email,
        string? description = null, bool emailOnSuccess = false, bool emailOnFailure = true);

    Task UpdateJobAsync(string userId, int id, string command, string schedule, string? description,
        string? email, bool emailOnSuccess, bool emailOnFailure);

    Task DeleteJobAsync(string userId, int id);
    Task EnableJobAsync(string userId, int id);
    Task DisableJobAsync(string userId, int id);

    /// <summary>Triggers an out-of-band execution. Returns the log row id.</summary>
    Task<int> RunNowAsync(string userId, int id);

    Task<List<CronJobLog>> GetJobLogAsync(string userId, int id, int limit = 25);

    CronValidation ValidateCronExpression(string expression);

    /// <summary>Remaining cron-job allowance for the user's package (null = unlimited).</summary>
    Task<(int used, int? limit)> GetQuotaAsync(string userId);
}

public class CronService : ICronService
{
    internal const string ServiceName = "cron";
    private readonly ApplicationDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;

    public CronService(ApplicationDbContext db, IServiceScopeFactory scopeFactory)
    {
        _db = db;
        _scopeFactory = scopeFactory;
    }

    public Task<List<CronJob>> GetJobsAsync(string userId) =>
        _db.CronJobs.Where(j => j.UserId == userId).OrderBy(j => j.CreatedAt).ToListAsync();

    public Task<CronJob?> GetJobAsync(string userId, int id) =>
        _db.CronJobs.FirstOrDefaultAsync(j => j.Id == id && j.UserId == userId);

    public CronValidation ValidateCronExpression(string expression)
    {
        if (!CronExpression.TryParse(expression, out var schedule, out var error) || schedule == null)
            return new CronValidation(false, "", error, null);

        return new CronValidation(true, schedule.Describe(), null, schedule.Next(DateTime.UtcNow));
    }

    public async Task<(int used, int? limit)> GetQuotaAsync(string userId)
    {
        var used = await _db.CronJobs.CountAsync(j => j.UserId == userId);
        var user = await _db.Users.Include(u => u.Package).FirstOrDefaultAsync(u => u.Id == userId);

        // No package assigned falls back to the default allowance rather than "unlimited".
        var max = user?.Package?.MaxCronJobs ?? DefaultLimit;
        return (used, max == 0 ? null : max);
    }

    private const int DefaultLimit = 10;

    public async Task<CronJob> CreateJobAsync(string userId, string command, string schedule, string? email,
        string? description = null, bool emailOnSuccess = false, bool emailOnFailure = true)
    {
        var validation = ValidateCronExpression(schedule);
        if (!validation.IsValid)
            throw new InvalidOperationException(validation.Error ?? "Invalid cron expression.");

        var (used, limit) = await GetQuotaAsync(userId);
        if (limit.HasValue && used >= limit.Value)
            throw new InvalidOperationException($"Your plan allows {limit} cron jobs. Delete one or upgrade to add more.");

        var sanitized = SanitizeCommand(command);

        var job = new CronJob
        {
            UserId = userId,
            Command = sanitized,
            Schedule = schedule.Trim(),
            Description = description,
            Email = email,
            EmailOnSuccess = emailOnSuccess,
            EmailOnFailure = emailOnFailure,
            IsActive = true,
            NextRunAt = validation.NextRun,
            CreatedAt = DateTime.UtcNow
        };

        _db.CronJobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    public async Task UpdateJobAsync(string userId, int id, string command, string schedule, string? description,
        string? email, bool emailOnSuccess, bool emailOnFailure)
    {
        var job = await GetJobAsync(userId, id) ?? throw new InvalidOperationException("Cron job not found.");

        var validation = ValidateCronExpression(schedule);
        if (!validation.IsValid)
            throw new InvalidOperationException(validation.Error ?? "Invalid cron expression.");

        job.Command = SanitizeCommand(command);
        job.Schedule = schedule.Trim();
        job.Description = description;
        job.Email = email;
        job.EmailOnSuccess = emailOnSuccess;
        job.EmailOnFailure = emailOnFailure;
        job.NextRunAt = job.IsActive ? validation.NextRun : null;

        await _db.SaveChangesAsync();
    }

    public async Task DeleteJobAsync(string userId, int id)
    {
        var job = await GetJobAsync(userId, id);
        if (job == null) return;
        _db.CronJobs.Remove(job);
        await _db.SaveChangesAsync();
    }

    public async Task EnableJobAsync(string userId, int id)
    {
        var job = await GetJobAsync(userId, id);
        if (job == null) return;
        job.IsActive = true;
        job.NextRunAt = ValidateCronExpression(job.Schedule).NextRun;
        await _db.SaveChangesAsync();
    }

    public async Task DisableJobAsync(string userId, int id)
    {
        var job = await GetJobAsync(userId, id);
        if (job == null) return;
        job.IsActive = false;
        job.NextRunAt = null;
        await _db.SaveChangesAsync();
    }

    public async Task<int> RunNowAsync(string userId, int id)
    {
        var job = await GetJobAsync(userId, id) ?? throw new InvalidOperationException("Cron job not found.");
        if (job.IsRunning) throw new InvalidOperationException("This job is already running.");

        var log = new CronJobLog { CronJobId = job.Id, StartedAt = DateTime.UtcNow, Manual = true };
        _db.CronJobLogs.Add(log);
        job.IsRunning = true;
        await _db.SaveChangesAsync();

        var jobId = job.Id;
        var logId = log.Id;
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            await ExecuteAsync(scope.ServiceProvider, jobId, logId);
        });

        return logId;
    }

    public Task<List<CronJobLog>> GetJobLogAsync(string userId, int id, int limit = 25) =>
        _db.CronJobLogs
            .Where(l => l.CronJobId == id && l.CronJob!.UserId == userId)
            .OrderByDescending(l => l.StartedAt)
            .Take(limit)
            .ToListAsync();

    /// <summary>
    /// Runs one job and records the result. Shared by the manual trigger and the scheduler.
    /// The command runs through ICommandRunner, so it is logged (and skipped) in simulation mode.
    /// </summary>
    internal static async Task ExecuteAsync(IServiceProvider sp, int jobId, int logId)
    {
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var runner = sp.GetRequiredService<ICommandRunner>();
        var mailer = sp.GetRequiredService<IMailerService>();
        var logger = sp.GetRequiredService<ILogger<CronService>>();

        var job = await db.CronJobs.FirstOrDefaultAsync(j => j.Id == jobId);
        var log = await db.CronJobLogs.FirstOrDefaultAsync(l => l.Id == logId);
        if (job == null || log == null) return;

        var sw = Stopwatch.StartNew();
        try
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == job.UserId);
            var home = $"/home/{user?.UserName ?? "user"}";

            // Sandbox: always execute from the user's home as the user.
            var wrapped = $"cd {home} && {job.Command}";
            var result = await runner.RunAsync(wrapped, ServiceName);

            sw.Stop();
            log.ExitCode = result.ExitCode;
            log.Output = Truncate(result.Simulated
                ? $"[SIMULATED] {wrapped}\n{result.Output}"
                : result.Output, 20000);
            log.CompletedAt = DateTime.UtcNow;
            log.DurationMs = sw.ElapsedMilliseconds;

            job.LastRunAt = log.StartedAt;
            job.LastExitCode = result.ExitCode;
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "Cron job {JobId} failed", jobId);
            log.ExitCode = 1;
            log.Output = $"ERROR: {ex.Message}";
            log.CompletedAt = DateTime.UtcNow;
            log.DurationMs = sw.ElapsedMilliseconds;
            job.LastRunAt = log.StartedAt;
            job.LastExitCode = 1;
        }
        finally
        {
            job.IsRunning = false;
            if (job.IsActive && CronExpression.TryParse(job.Schedule, out var schedule, out _) && schedule != null)
                job.NextRunAt = schedule.Next(DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        // Notify per the job's email preferences.
        var failed = job.LastExitCode != 0;
        if (!string.IsNullOrWhiteSpace(job.Email) && ((failed && job.EmailOnFailure) || (!failed && job.EmailOnSuccess)))
        {
            await mailer.SendTemplateAsync(job.Email!,
                failed ? $"Cron job failed: {job.Command}" : $"Cron job succeeded: {job.Command}",
                "cron_report",
                new Dictionary<string, string>
                {
                    ["COMMAND"] = job.Command,
                    ["SCHEDULE"] = job.Schedule,
                    ["STATUS"] = failed ? "FAILED" : "SUCCESS",
                    ["EXIT_CODE"] = job.LastExitCode?.ToString() ?? "?",
                    ["DURATION"] = $"{log.DurationMs} ms",
                    ["OUTPUT"] = Truncate(log.Output, 2000),
                    ["RAN_AT"] = log.StartedAt.ToString("u")
                });
        }

        if (failed)
        {
            var notifications = sp.GetRequiredService<INotificationService>();
            await notifications.NotifyAsync(job.UserId, "Cron job failed",
                $"'{job.Command}' exited with code {job.LastExitCode}.", NotificationType.Error,
                $"cron-fail-{job.Id}");
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "\n… (truncated)";

    /// <summary>
    /// Rejects command syntax that would escape the user's home directory or chain into
    /// privileged commands. The scheduler always prefixes a `cd $HOME`.
    /// </summary>
    internal static string SanitizeCommand(string command)
    {
        var cmd = (command ?? string.Empty).Trim();
        if (cmd.Length == 0) throw new InvalidOperationException("Enter a command to run.");
        if (cmd.Length > 500) throw new InvalidOperationException("Command is too long (max 500 characters).");
        if (cmd.Contains("..")) throw new InvalidOperationException("Paths containing '..' are not allowed.");

        var blocked = new[] { "sudo", "su ", "chown", "chmod 777", "mkfs", "shutdown", "reboot", "systemctl", "iptables" };
        foreach (var word in blocked)
        {
            if (cmd.Contains(word, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"'{word.Trim()}' is not allowed in cron commands.");
        }

        // Absolute paths are only permitted into the user's own tree and the standard bin dirs.
        if (cmd.StartsWith('/') && !cmd.StartsWith("/home/") && !cmd.StartsWith("/usr/bin/") && !cmd.StartsWith("/bin/"))
            throw new InvalidOperationException("Absolute commands must live under /home, /usr/bin or /bin.");

        return cmd;
    }
}

/// <summary>
/// Fires due cron jobs once a minute and reaps stale execution logs.
/// Every job runs through ICommandRunner, so on a dev host the commands are logged, not executed.
/// </summary>
public class CronBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CronBackgroundService> _logger;

    public CronBackgroundService(IServiceScopeFactory scopeFactory, ILogger<CronBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the database migrate and seed before the first tick.
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Cron scheduler tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var due = await db.CronJobs
            .Where(j => j.IsActive && !j.IsRunning && j.NextRunAt != null && j.NextRunAt <= now)
            .ToListAsync(ct);

        foreach (var job in due)
        {
            var log = new CronJobLog { CronJobId = job.Id, StartedAt = now };
            db.CronJobLogs.Add(log);
            job.IsRunning = true;
            await db.SaveChangesAsync(ct);

            var jobId = job.Id;
            var logId = log.Id;
            _ = Task.Run(async () =>
            {
                using var runScope = _scopeFactory.CreateScope();
                await CronService.ExecuteAsync(runScope.ServiceProvider, jobId, logId);
            }, ct);
        }

        // Backfill NextRunAt for jobs enabled before the scheduler last ran.
        var missing = await db.CronJobs.Where(j => j.IsActive && j.NextRunAt == null && !j.IsRunning).ToListAsync(ct);
        foreach (var job in missing)
        {
            if (CronExpression.TryParse(job.Schedule, out var schedule, out _) && schedule != null)
                job.NextRunAt = schedule.Next(now);
        }
        if (missing.Count > 0) await db.SaveChangesAsync(ct);

        // Retain the 50 most recent runs per job.
        var noisy = await db.CronJobLogs
            .GroupBy(l => l.CronJobId)
            .Where(g => g.Count() > 50)
            .Select(g => g.Key)
            .ToListAsync(ct);

        foreach (var cronJobId in noisy)
        {
            var stale = await db.CronJobLogs
                .Where(l => l.CronJobId == cronJobId)
                .OrderByDescending(l => l.StartedAt)
                .Skip(50)
                .ToListAsync(ct);
            db.CronJobLogs.RemoveRange(stale);
        }
        if (noisy.Count > 0) await db.SaveChangesAsync(ct);
    }
}
