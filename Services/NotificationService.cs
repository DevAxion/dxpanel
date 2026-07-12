using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Services;

public interface INotificationService
{
    Task NotifyAsync(string userId, string title, string message, NotificationType type = NotificationType.Info, string? dedupeKey = null);
    Task<int> GetUnreadCountAsync(string userId);
    Task<List<Notification>> GetRecentAsync(string userId, int count = 5);
    Task MarkAsReadAsync(string userId, int notificationId);
    Task MarkAllAsReadAsync(string userId);
}

public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _db;

    public NotificationService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task NotifyAsync(string userId, string title, string message, NotificationType type = NotificationType.Info, string? dedupeKey = null)
    {
        if (dedupeKey != null)
        {
            var exists = await _db.Notifications
                .AnyAsync(n => n.UserId == userId && n.DedupeKey == dedupeKey && !n.IsRead);
            if (exists) return;
        }

        _db.Notifications.Add(new Notification
        {
            UserId = userId,
            Title = title,
            Message = message,
            Type = type,
            DedupeKey = dedupeKey,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    public Task<int> GetUnreadCountAsync(string userId) =>
        _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);

    public Task<List<Notification>> GetRecentAsync(string userId, int count = 5) =>
        _db.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(count)
            .ToListAsync();

    public async Task MarkAsReadAsync(string userId, int notificationId)
    {
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == notificationId && x.UserId == userId);
        if (n != null && !n.IsRead)
        {
            n.IsRead = true;
            await _db.SaveChangesAsync();
        }
    }

    public async Task MarkAllAsReadAsync(string userId)
    {
        var unread = await _db.Notifications.Where(n => n.UserId == userId && !n.IsRead).ToListAsync();
        foreach (var n in unread) n.IsRead = true;
        if (unread.Count > 0) await _db.SaveChangesAsync();
    }
}
