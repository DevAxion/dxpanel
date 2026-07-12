using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Billing;

/// <summary>
/// Orchestrates account provisioning after a successful payment, and
/// suspension/reactivation on payment failure/recovery. All the underlying
/// Linux commands run through the integration services, so this is fully
/// simulation-safe on Windows/dev.
/// </summary>
public interface IProvisioningService
{
    Task ProvisionAsync(ApplicationUser user, Plan plan, string? generatedPassword = null);
    Task SuspendAsync(ApplicationUser user, string reason);
    Task ReactivateAsync(ApplicationUser user);

    /// <summary>Applies a new plan's quotas to an existing account (upgrade/downgrade) and updates disk quota + nginx.</summary>
    Task ApplyPlanChangeAsync(ApplicationUser user, Plan newPlan);

    /// <summary>Applies a client's total effective disk quota (plan + add-ons) to the filesystem.</summary>
    Task ApplyDiskQuotaAsync(ApplicationUser user, long totalDiskMB);
}

public class ProvisioningService : IProvisioningService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly INginxService _nginx;
    private readonly IMySqlService _mysql;
    private readonly IFtpService _ftp;
    private readonly ICommandRunner _runner;
    private readonly IMailerService _mailer;
    private readonly INotificationService _notifications;
    private readonly PanelSettings _panel;
    private readonly ILogger<ProvisioningService> _logger;

    public ProvisioningService(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        INginxService nginx, IMySqlService mysql, IFtpService ftp, ICommandRunner runner,
        IMailerService mailer, INotificationService notifications, IOptionsMonitor<PanelSettings> panel,
        ILogger<ProvisioningService> logger)
    {
        _db = db;
        _userManager = userManager;
        _nginx = nginx;
        _mysql = mysql;
        _ftp = ftp;
        _runner = runner;
        _mailer = mailer;
        _notifications = notifications;
        _panel = panel.CurrentValue;
        _logger = logger;
    }

    public async Task ProvisionAsync(ApplicationUser user, Plan plan, string? generatedPassword = null)
    {
        var username = HostingHelpers.UserPrefix(user.UserName ?? user.Email ?? "user");
        var homeDir = $"/home/{username}/public_html";
        var mysqlUser = HostingHelpers.Prefixed(user.UserName ?? username, "db");
        var ftpPassword = generatedPassword ?? HostingHelpers.GeneratePassword();

        // 1. Apply plan quotas to the user
        user.DiskQuotaMB = plan.DiskQuotaMB;
        user.BandwidthQuotaMB = plan.BandwidthQuotaMB;
        user.IsActive = true;
        await _userManager.UpdateAsync(user);

        // 2. Create system user + home directory (vsftpd service handles useradd -m -d)
        await _ftp.CreateFtpUserAsync(username, ftpPassword, homeDir);
        await _runner.RunAsync($"mkdir -p {homeDir} && chown -R {username}:{username} /home/{username}", "provisioning");

        // 3. Nginx virtual host for the user's primary domain (or a placeholder)
        var primaryDomain = await _db.Domains.Where(d => d.UserId == user.Id)
            .OrderBy(d => d.Id).Select(d => d.DomainName).FirstOrDefaultAsync()
            ?? $"{username}.{_panel.Hostname}";
        await _nginx.CreateVirtualHostAsync(primaryDomain, homeDir, plan.MaxDomains >= 0 ? _panel.DefaultPhpVersion : _panel.DefaultPhpVersion);

        // 4. MySQL user (prefixed)
        await _mysql.CreateUserAsync(mysqlUser, HostingHelpers.GeneratePassword());

        // 5. Disk quota
        await _ftp.SetQuotaAsync(username, plan.DiskQuotaMB);

        // 6. Welcome email
        await _mailer.SendTemplateAsync(user.Email ?? "", "Welcome to SRXPanel — your account is ready", "welcome",
            new Dictionary<string, string>
            {
                ["NAME"] = user.FullName ?? user.UserName ?? "there",
                ["PLAN"] = plan.Name,
                ["LOGIN_URL"] = $"https://{_panel.Hostname}/Account/Login",
                ["USERNAME"] = user.UserName ?? username,
                ["PASSWORD"] = "(the password you set at registration)",
                ["FTP_HOST"] = _panel.Hostname,
                ["FTP_USER"] = username,
                ["HOME_DIR"] = homeDir,
                ["MYSQL_HOST"] = _panel.MySql.Host,
                ["MYSQL_USER"] = mysqlUser,
                ["NS1"] = _panel.Bind.DefaultNs,
                ["NS2"] = "ns2." + _panel.Hostname,
                ["GUIDE_URL"] = $"https://{_panel.Hostname}/Help"
            });

        await _notifications.NotifyAsync(user.Id, "Account provisioned",
            $"Your {plan.Name} hosting account is active. Welcome aboard!", NotificationType.Success);

        _logger.LogInformation("Provisioned account for {User} on plan {Plan}", user.Email, plan.Name);
    }

    public async Task ApplyPlanChangeAsync(ApplicationUser user, Plan newPlan)
    {
        var username = HostingHelpers.UserPrefix(user.UserName ?? user.Email ?? "user");

        user.DiskQuotaMB = newPlan.DiskQuotaMB;
        user.BandwidthQuotaMB = newPlan.BandwidthQuotaMB;
        await _userManager.UpdateAsync(user);

        await _ftp.SetQuotaAsync(username, newPlan.DiskQuotaMB);
        await _runner.RunAsync("systemctl reload nginx", "provisioning");

        await _notifications.NotifyAsync(user.Id, "Plan updated",
            $"Your plan is now {newPlan.Name}. New limits are active immediately.", NotificationType.Success);
        _logger.LogInformation("Applied plan change for {User} -> {Plan}", user.Email, newPlan.Name);
    }

    public async Task ApplyDiskQuotaAsync(ApplicationUser user, long totalDiskMB)
    {
        var username = HostingHelpers.UserPrefix(user.UserName ?? user.Email ?? "user");
        user.DiskQuotaMB = totalDiskMB;
        await _userManager.UpdateAsync(user);
        await _ftp.SetQuotaAsync(username, totalDiskMB);
    }

    public async Task SuspendAsync(ApplicationUser user, string reason)
    {
        var username = HostingHelpers.UserPrefix(user.UserName ?? user.Email ?? "user");

        user.IsActive = false;
        await _userManager.UpdateAsync(user);

        // Disable nginx vhosts for the user's domains (rename .conf -> .conf.disabled), keep data intact.
        var domains = await _db.Domains.Where(d => d.UserId == user.Id).Select(d => d.DomainName).ToListAsync();
        foreach (var domain in domains)
        {
            await _runner.RunAsync(
                $"mv {_panel.Nginx.SitesAvailable}/{domain}.conf {_panel.Nginx.SitesAvailable}/{domain}.conf.disabled 2>/dev/null; " +
                $"rm -f {_panel.Nginx.SitesEnabled}/{domain}.conf",
                "provisioning");
        }
        await _runner.RunAsync("systemctl reload nginx", "provisioning");

        await _mailer.SendTemplateAsync(user.Email ?? "", "Your SRXPanel account has been suspended", "suspension",
            new Dictionary<string, string>
            {
                ["NAME"] = user.FullName ?? user.UserName ?? "there",
                ["PAY_URL"] = $"https://{_panel.Hostname}/Billing",
                ["DELETION_NOTICE"] = "Data is retained for 14 days. After that it may be scheduled for deletion."
            });

        await _notifications.NotifyAsync(user.Id, "Account suspended", reason, NotificationType.Error);
        _logger.LogInformation("Suspended account for {User}: {Reason}", user.Email, reason);
    }

    public async Task ReactivateAsync(ApplicationUser user)
    {
        user.IsActive = true;
        await _userManager.UpdateAsync(user);

        var domains = await _db.Domains.Where(d => d.UserId == user.Id).Select(d => d.DomainName).ToListAsync();
        foreach (var domain in domains)
        {
            await _runner.RunAsync(
                $"mv {_panel.Nginx.SitesAvailable}/{domain}.conf.disabled {_panel.Nginx.SitesAvailable}/{domain}.conf 2>/dev/null; " +
                $"ln -sf {_panel.Nginx.SitesAvailable}/{domain}.conf {_panel.Nginx.SitesEnabled}/{domain}.conf",
                "provisioning");
        }
        await _runner.RunAsync("systemctl reload nginx", "provisioning");

        var sub = await _db.Subscriptions.Include(s => s.Plan)
            .Where(s => s.UserId == user.Id).OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync();

        await _mailer.SendTemplateAsync(user.Email ?? "", "Welcome back — your account is active", "reactivation",
            new Dictionary<string, string>
            {
                ["NAME"] = user.FullName ?? user.UserName ?? "there",
                ["PLAN"] = sub?.Plan?.Name ?? "your plan",
                ["NEXT_BILLING"] = sub?.CurrentPeriodEnd.ToString("yyyy-MM-dd") ?? "-",
                ["LOGIN_URL"] = $"https://{_panel.Hostname}/Account/Login"
            });

        await _notifications.NotifyAsync(user.Id, "Account reactivated", "Your services are back online.", NotificationType.Success);
        _logger.LogInformation("Reactivated account for {User}", user.Email);
    }
}
