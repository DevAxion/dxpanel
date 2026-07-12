using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Services;

/// <summary>
/// Centralizes role-based ownership scoping so every hosting module
/// resolves "which users can I see/manage" the same way.
/// </summary>
public interface IUserScopeService
{
    Task<ApplicationUser?> GetCurrentUserAsync(ClaimsPrincipal principal);

    /// <summary>Returns the set of user IDs the caller may manage (including themselves).</summary>
    Task<HashSet<string>> GetManageableUserIdsAsync(ClaimsPrincipal principal);

    /// <summary>Returns the users the caller may assign resources to / filter by.</summary>
    Task<List<ApplicationUser>> GetManageableUsersAsync(ClaimsPrincipal principal);

    Task<bool> CanManageUserAsync(ClaimsPrincipal principal, string targetUserId);
}

public class UserScopeService : IUserScopeService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public UserScopeService(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public Task<ApplicationUser?> GetCurrentUserAsync(ClaimsPrincipal principal) =>
        _userManager.GetUserAsync(principal)!;

    public async Task<HashSet<string>> GetManageableUserIdsAsync(ClaimsPrincipal principal)
    {
        var users = await GetManageableUsersAsync(principal);
        return users.Select(u => u.Id).ToHashSet();
    }

    public async Task<List<ApplicationUser>> GetManageableUsersAsync(ClaimsPrincipal principal)
    {
        var current = await _userManager.GetUserAsync(principal);
        if (current == null) return new List<ApplicationUser>();

        if (principal.IsInRole(Roles.SuperAdmin))
        {
            return await _db.Users.ToListAsync();
        }

        if (principal.IsInRole(Roles.Reseller))
        {
            return await _db.Users
                .Where(u => u.Id == current.Id || u.ResellerId == current.Id)
                .ToListAsync();
        }

        return new List<ApplicationUser> { current };
    }

    public async Task<bool> CanManageUserAsync(ClaimsPrincipal principal, string targetUserId)
    {
        if (principal.IsInRole(Roles.SuperAdmin)) return true;

        var current = await _userManager.GetUserAsync(principal);
        if (current == null) return false;

        if (current.Id == targetUserId) return true;

        if (principal.IsInRole(Roles.Reseller))
        {
            var target = await _db.Users.FirstOrDefaultAsync(u => u.Id == targetUserId);
            return target != null && target.ResellerId == current.Id;
        }

        return false;
    }
}
