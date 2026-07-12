using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Billing;

namespace SRXPanel.Services.Api;

/// <summary>
/// Account operations shared by the WHMCS / Blesta / REST integration endpoints.
/// Provisioning runs through the simulation-safe integration services.
/// </summary>
public interface IApiAccountService
{
    Task<(bool Ok, string Message, object? Data)> CreateAccountAsync(string username, string password, string email, string plan, string? domain);
    Task<(bool Ok, string Message)> SuspendAsync(string username, string? reason);
    Task<(bool Ok, string Message)> UnsuspendAsync(string username);
    Task<(bool Ok, string Message)> TerminateAsync(string username);
    Task<(bool Ok, string Message)> ChangePasswordAsync(string username, string newPassword);
    Task<(bool Ok, string Message)> ChangePackageAsync(string username, string newPlan);
    Task<(bool Ok, object? Data)> GetUsageAsync(string username);
}

public class ApiAccountService : IApiAccountService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IProvisioningService _provisioning;
    private readonly IFileManagerService _files;

    public ApiAccountService(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        IProvisioningService provisioning, IFileManagerService files)
    {
        _db = db;
        _userManager = userManager;
        _provisioning = provisioning;
        _files = files;
    }

    private Task<ApplicationUser?> FindAsync(string username) =>
        _userManager.FindByNameAsync(username)!;

    public async Task<(bool Ok, string Message, object? Data)> CreateAccountAsync(string username, string password, string email, string plan, string? domain)
    {
        if (await _userManager.FindByNameAsync(username) != null)
            return (false, "Account already exists", null);

        var planEntity = await _db.Plans.FirstOrDefaultAsync(p => p.Name == plan);
        if (planEntity == null)
            return (false, $"Plan '{plan}' not found", null);

        var user = new ApplicationUser
        {
            UserName = username,
            Email = email,
            FullName = username,
            EmailConfirmed = true,
            IsActive = true,
            DiskQuotaMB = planEntity.DiskQuotaMB,
            BandwidthQuotaMB = planEntity.BandwidthQuotaMB,
            CreatedAt = DateTime.UtcNow
        };
        var result = await _userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            return (false, string.Join("; ", result.Errors.Select(e => e.Description)), null);

        await _userManager.AddToRoleAsync(user, Roles.Client);

        if (!string.IsNullOrWhiteSpace(domain))
        {
            _db.Domains.Add(new Domain
            {
                UserId = user.Id,
                DomainName = domain.Trim().ToLowerInvariant(),
                DocumentRoot = $"/home/{HostingHelpers.UserPrefix(username)}/public_html/{domain}",
                PhpVersion = "8.3",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }

        await _provisioning.ProvisionAsync(user, planEntity, password);
        return (true, "Account created", new { username, email, plan, domain });
    }

    public async Task<(bool Ok, string Message)> SuspendAsync(string username, string? reason)
    {
        var user = await FindAsync(username);
        if (user == null) return (false, "Account not found");
        user.SuspensionReason = reason ?? "Suspended via API";
        await _provisioning.SuspendAsync(user, reason ?? "Suspended via API");
        return (true, "Account suspended");
    }

    public async Task<(bool Ok, string Message)> UnsuspendAsync(string username)
    {
        var user = await FindAsync(username);
        if (user == null) return (false, "Account not found");
        user.SuspensionReason = null;
        await _provisioning.ReactivateAsync(user);
        return (true, "Account reactivated");
    }

    public async Task<(bool Ok, string Message)> TerminateAsync(string username)
    {
        var user = await FindAsync(username);
        if (user == null) return (false, "Account not found");
        var result = await _userManager.DeleteAsync(user);
        return result.Succeeded ? (true, "Account terminated") : (false, "Failed to terminate account");
    }

    public async Task<(bool Ok, string Message)> ChangePasswordAsync(string username, string newPassword)
    {
        var user = await FindAsync(username);
        if (user == null) return (false, "Account not found");
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        return result.Succeeded ? (true, "Password changed")
            : (false, string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    public async Task<(bool Ok, string Message)> ChangePackageAsync(string username, string newPlan)
    {
        var user = await FindAsync(username);
        if (user == null) return (false, "Account not found");
        var plan = await _db.Plans.FirstOrDefaultAsync(p => p.Name == newPlan);
        if (plan == null) return (false, $"Plan '{newPlan}' not found");

        user.DiskQuotaMB = plan.DiskQuotaMB;
        user.BandwidthQuotaMB = plan.BandwidthQuotaMB;
        await _userManager.UpdateAsync(user);
        return (true, $"Package changed to {newPlan}");
    }

    public async Task<(bool Ok, object? Data)> GetUsageAsync(string username)
    {
        var user = await FindAsync(username);
        if (user == null) return (false, null);
        var data = new
        {
            disk_used = _files.GetUsedBytes(user.Id) / 1024 / 1024,
            disk_quota = user.DiskQuotaMB,
            bandwidth_used = 0,
            bandwidth_quota = user.BandwidthQuotaMB,
            email_count = await _db.EmailAccounts.CountAsync(e => e.UserId == user.Id),
            domain_count = await _db.Domains.CountAsync(d => d.UserId == user.Id),
            database_count = await _db.Databases.CountAsync(d => d.UserId == user.Id)
        };
        return (true, data);
    }
}
