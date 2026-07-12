using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Services.Portal;

public interface ITicketService
{
    Task<Ticket> CreateAsync(string userId, string subject, TicketPriority priority, string message, string? attachments);
    Task<TicketReply> ReplyAsync(int ticketId, string userId, string message, bool isStaff, string? attachments);
    Task SetStatusAsync(int ticketId, TicketStatus status);
    Task SetPriorityAsync(int ticketId, TicketPriority priority);
    Task AssignAsync(int ticketId, string? staffId);
    Task<int> OpenCountAsync(string userId);
}

public class TicketService : ITicketService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly INotificationService _notifications;

    public TicketService(ApplicationDbContext db, UserManager<ApplicationUser> userManager, INotificationService notifications)
    {
        _db = db;
        _userManager = userManager;
        _notifications = notifications;
    }

    public async Task<Ticket> CreateAsync(string userId, string subject, TicketPriority priority, string message, string? attachments)
    {
        var ticket = new Ticket
        {
            UserId = userId,
            Subject = subject,
            Priority = priority,
            Status = TicketStatus.Open,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        ticket.Replies.Add(new TicketReply
        {
            UserId = userId,
            Message = message,
            IsStaff = false,
            Attachments = attachments,
            CreatedAt = DateTime.UtcNow
        });
        _db.Tickets.Add(ticket);
        await _db.SaveChangesAsync();

        // Notify all SuperAdmins of the new ticket.
        var admins = await _userManager.GetUsersInRoleAsync(Roles.SuperAdmin);
        foreach (var admin in admins)
        {
            await _notifications.NotifyAsync(admin.Id, "New support ticket",
                $"#{ticket.Id}: {subject}", NotificationType.Info, dedupeKey: $"ticket-new-{ticket.Id}");
        }
        return ticket;
    }

    public async Task<TicketReply> ReplyAsync(int ticketId, string userId, string message, bool isStaff, string? attachments)
    {
        var ticket = await _db.Tickets.FirstAsync(t => t.Id == ticketId);
        var reply = new TicketReply
        {
            TicketId = ticketId,
            UserId = userId,
            Message = message,
            IsStaff = isStaff,
            Attachments = attachments,
            CreatedAt = DateTime.UtcNow
        };
        _db.TicketReplies.Add(reply);

        ticket.UpdatedAt = DateTime.UtcNow;
        if (isStaff && ticket.Status == TicketStatus.Open) ticket.Status = TicketStatus.InProgress;
        if (!isStaff && ticket.Status == TicketStatus.Closed) ticket.Status = TicketStatus.Open;

        await _db.SaveChangesAsync();

        // Staff replies notify the customer.
        if (isStaff)
        {
            await _notifications.NotifyAsync(ticket.UserId, "Support replied",
                $"New reply on ticket #{ticket.Id}: {ticket.Subject}", NotificationType.Info);
        }
        return reply;
    }

    public async Task SetStatusAsync(int ticketId, TicketStatus status)
    {
        var ticket = await _db.Tickets.FindAsync(ticketId);
        if (ticket == null) return;
        ticket.Status = status;
        ticket.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task SetPriorityAsync(int ticketId, TicketPriority priority)
    {
        var ticket = await _db.Tickets.FindAsync(ticketId);
        if (ticket == null) return;
        ticket.Priority = priority;
        ticket.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task AssignAsync(int ticketId, string? staffId)
    {
        var ticket = await _db.Tickets.FindAsync(ticketId);
        if (ticket == null) return;
        ticket.AssignedToId = staffId;
        if (staffId != null && ticket.Status == TicketStatus.Open) ticket.Status = TicketStatus.InProgress;
        ticket.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public Task<int> OpenCountAsync(string userId) =>
        _db.Tickets.CountAsync(t => t.UserId == userId && t.Status != TicketStatus.Closed);
}
