using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Services.Reseller;

public enum ResourceKind { Domain, Email, Database, Ftp, Dns, Backup }

/// <summary>
/// Enforces reseller-level constraints when a client provisions a resource:
/// the owning reseller must be active, must permit the feature, and must have
/// remaining aggregate quota. Per-plan limits are still enforced by each page.
/// SuperAdmin actions bypass this guard.
/// </summary>
public interface IResourceGuard
{
    Task<(bool Ok, string? Error)> CheckAsync(ApplicationUser client, ResourceKind kind);
}

public class ResourceGuard : IResourceGuard
{
    private readonly ApplicationDbContext _db;

    public ResourceGuard(ApplicationDbContext db) => _db = db;

    public async Task<(bool Ok, string? Error)> CheckAsync(ApplicationUser client, ResourceKind kind)
    {
        if (string.IsNullOrEmpty(client.ResellerId))
            return (true, null); // Direct/admin client — no reseller ceiling.

        var profile = await _db.ResellerProfiles.FirstOrDefaultAsync(p => p.UserId == client.ResellerId);
        if (profile == null)
            return (true, null);

        if (!profile.IsActive)
            return (false, "Your hosting provider's account is suspended. Please contact support.");

        // Feature grants
        switch (kind)
        {
            case ResourceKind.Email when !profile.AllowEmail:
                return (false, "Email hosting is not available on your account.");
            case ResourceKind.Dns when !profile.AllowDns:
                return (false, "DNS management is not available on your account.");
            case ResourceKind.Backup when !profile.AllowBackups:
                return (false, "Backups are not available on your account.");
        }

        // Aggregate ceiling: domains counted across all of the reseller's clients.
        if (kind == ResourceKind.Domain && profile.MaxDomains > 0)
        {
            var clientIds = await _db.Users.Where(u => u.ResellerId == profile.UserId)
                .Select(u => u.Id).ToListAsync();
            var used = await _db.Domains.CountAsync(d => clientIds.Contains(d.UserId));
            if (used >= profile.MaxDomains)
                return (false, "Your hosting provider has reached its domain allocation. Please contact support.");
        }

        return (true, null);
    }
}
