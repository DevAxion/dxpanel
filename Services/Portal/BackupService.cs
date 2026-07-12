using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Portal;

/// <summary>
/// Creates account backups. In simulation mode the tar/mysqldump commands are
/// logged (via ICommandRunner) and a backup archive record is created with a
/// simulated size; on Linux the real commands run.
/// </summary>
public interface IBackupService
{
    Task<Backup> CreateBackupAsync(string userId, string userName, BackupType type);
    Task<ServiceResult> RestoreAsync(string userId, int backupId);
    Task<BackupSchedule> GetOrCreateScheduleAsync(string userId);
    Task SaveScheduleAsync(BackupSchedule schedule);
    Task EnforceRetentionAsync(string userId, int keep);

    /// <summary>Max on-panel backups the user's plan allows (0 = unlimited).</summary>
    Task<int> GetBackupLimitAsync(string userId);

    /// <summary>Whether the user may create another backup under their plan limit.</summary>
    Task<bool> CanCreateBackupAsync(string userId);
}

public class BackupService : IBackupService
{
    private const string ServiceName = "backup";
    private readonly ApplicationDbContext _db;
    private readonly ICommandRunner _runner;
    private readonly IWebHostEnvironment _env;

    public BackupService(ApplicationDbContext db, ICommandRunner runner, IWebHostEnvironment env)
    {
        _db = db;
        _runner = runner;
        _env = env;
    }

    public async Task<Backup> CreateBackupAsync(string userId, string userName, BackupType type)
    {
        var prefix = HostingHelpers.UserPrefix(userName);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var fileName = $"{prefix}-{type.ToString().ToLowerInvariant()}-{stamp}.tar.gz";

        var backup = new Backup
        {
            UserId = userId,
            Type = type,
            Status = BackupStatus.Running,
            FilePath = $"/var/backups/srxpanel/{fileName}",
            CreatedAt = DateTime.UtcNow
        };
        _db.Backups.Add(backup);
        await _db.SaveChangesAsync();

        // Log the would-be backup commands.
        if (type is BackupType.Full or BackupType.Files)
            await _runner.RunAsync($"tar czf /var/backups/srxpanel/{fileName} /home/{prefix}", ServiceName);
        if (type is BackupType.Full or BackupType.Databases)
            await _runner.RunAsync($"mysqldump --all-databases | gzip > /var/backups/srxpanel/{prefix}-db-{stamp}.sql.gz", ServiceName);
        if (type is BackupType.Full or BackupType.Emails)
            await _runner.RunAsync($"tar czf /var/backups/srxpanel/{prefix}-mail-{stamp}.tar.gz /var/mail/vhosts", ServiceName);

        // In simulation create a small placeholder archive locally so it can be downloaded.
        if (_runner.SimulationMode)
        {
            var dir = Path.Combine(_env.ContentRootPath, "App_Data", "backups");
            Directory.CreateDirectory(dir);
            var localPath = Path.Combine(dir, fileName.Replace(".tar.gz", ".txt"));
            await File.WriteAllTextAsync(localPath,
                $"[SIMULATED BACKUP]\nUser: {userName}\nType: {type}\nCreated: {DateTime.UtcNow:O}\n" +
                "In production this would be a tar.gz of files + mysqldump + maildir.");
            backup.FilePath = localPath;
            backup.SizeBytes = new FileInfo(localPath).Length + Random.Shared.Next(50_000_000, 500_000_000);
        }
        else
        {
            backup.SizeBytes = File.Exists(backup.FilePath) ? new FileInfo(backup.FilePath).Length : 0;
        }

        backup.Status = BackupStatus.Completed;
        backup.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return backup;
    }

    public async Task<ServiceResult> RestoreAsync(string userId, int backupId)
    {
        var backup = await _db.Backups.FirstOrDefaultAsync(b => b.Id == backupId && b.UserId == userId);
        if (backup == null) return ServiceResult.Fail("Backup not found.");

        var cmd = await _runner.RunAsync(
            $"tar xzf {backup.FilePath} -C / && mysql < restore.sql", ServiceName);
        return new ServiceResult
        {
            Success = true,
            Message = cmd.Simulated ? "Restore simulated (no data changed)." : "Restore completed.",
            Commands = { cmd }
        };
    }

    public async Task<BackupSchedule> GetOrCreateScheduleAsync(string userId)
    {
        var schedule = await _db.BackupSchedules.FirstOrDefaultAsync(s => s.UserId == userId);
        if (schedule == null)
        {
            schedule = new BackupSchedule { UserId = userId };
            _db.BackupSchedules.Add(schedule);
            await _db.SaveChangesAsync();
        }
        return schedule;
    }

    public async Task SaveScheduleAsync(BackupSchedule schedule)
    {
        _db.BackupSchedules.Update(schedule);
        await _db.SaveChangesAsync();
    }

    public async Task<int> GetBackupLimitAsync(string userId)
    {
        // Plan-based limit comes from the assigned admin Package (0 = unlimited).
        // Users without a package assigned are treated as unlimited.
        var packageId = await _db.Users.Where(u => u.Id == userId).Select(u => u.PackageId).FirstOrDefaultAsync();
        if (packageId == null) return 0;
        return await _db.Packages.Where(p => p.Id == packageId).Select(p => p.MaxBackups).FirstOrDefaultAsync();
    }

    public async Task<bool> CanCreateBackupAsync(string userId)
    {
        var limit = await GetBackupLimitAsync(userId);
        if (limit <= 0) return true; // unlimited
        var count = await _db.Backups.CountAsync(b => b.UserId == userId);
        return count < limit;
    }

    public async Task EnforceRetentionAsync(string userId, int keep)
    {
        var backups = await _db.Backups.Where(b => b.UserId == userId)
            .OrderByDescending(b => b.CreatedAt).ToListAsync();
        foreach (var old in backups.Skip(keep))
        {
            try { if (old.FilePath != null && File.Exists(old.FilePath)) File.Delete(old.FilePath); } catch { }
            _db.Backups.Remove(old);
        }
        await _db.SaveChangesAsync();
    }
}
