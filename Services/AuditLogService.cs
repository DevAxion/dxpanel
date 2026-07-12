using Microsoft.AspNetCore.Http;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Services;

public interface IAuditLogService
{
    Task LogAsync(string action, string entity, string? entityId, string? details = null);
}

public class AuditLogService : IAuditLogService
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditLogService(ApplicationDbContext db, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task LogAsync(string action, string entity, string? entityId, string? details = null)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var user = httpContext?.User;

        var log = new AuditLog
        {
            UserId = user?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            UserName = user?.Identity?.IsAuthenticated == true ? (user.Identity.Name ?? "unknown") : "system",
            Action = action,
            Entity = entity,
            EntityId = entityId,
            Details = details,
            IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            Timestamp = DateTime.UtcNow
        };

        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync();
    }
}
