using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;

namespace SRXPanel.Services.Billing;

/// <summary>
/// Periodically:
///  - sends trial reminder emails (7 days & 1 day before trial ends)
///  - auto-suspends expired trials without payment
///  - escalates past-due dunning: 7-day final warning, 14-day deletion warning + suspend
/// </summary>
public class TrialDunningBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TrialDunningBackgroundService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    public TrialDunningBackgroundService(IServiceScopeFactory scopeFactory, ILogger<TrialDunningBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small startup delay so the DB is migrated/seeded first.
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); } catch { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Trial/dunning sweep failed");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var mailer = scope.ServiceProvider.GetRequiredService<IMailerService>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var provisioning = scope.ServiceProvider.GetRequiredService<IProvisioningService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var panel = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<PanelSettings>>().CurrentValue;
        var now = DateTime.UtcNow;

        var trials = await db.Subscriptions.Include(s => s.Plan)
            .Where(s => s.Status == SubscriptionStatus.Trialing && s.TrialEndsAt != null)
            .ToListAsync(ct);

        foreach (var sub in trials)
        {
            var daysLeft = (int)Math.Ceiling((sub.TrialEndsAt!.Value - now).TotalDays);
            var user = await userManager.FindByIdAsync(sub.UserId);
            if (user == null) continue;

            if (daysLeft <= 0)
            {
                // Trial expired without payment → suspend
                await provisioning.SuspendAsync(user, "Your free trial has ended. Add a payment method to continue.");
                sub.Status = SubscriptionStatus.PastDue;
                sub.PastDueSince = now;
            }
            else if (daysLeft == 7 || daysLeft == 1)
            {
                await mailer.SendTemplateAsync(user.Email ?? "", $"Your trial ends in {daysLeft} day(s)", "trial_reminder",
                    new Dictionary<string, string>
                    {
                        ["NAME"] = user.FullName ?? user.UserName ?? "there",
                        ["PLAN"] = sub.Plan?.Name ?? "your plan",
                        ["TRIAL_END"] = sub.TrialEndsAt.Value.ToString("yyyy-MM-dd"),
                        ["DAYS_LEFT"] = daysLeft.ToString(),
                        ["WHEN"] = daysLeft == 1 ? "tomorrow" : $"in {daysLeft} days",
                        ["BILLING_URL"] = $"https://{panel.Hostname}/Billing"
                    });
                await notifications.NotifyAsync(user.Id, "Trial ending soon",
                    $"Your free trial ends in {daysLeft} day(s). Add a payment method to keep your services.",
                    NotificationType.Warning, dedupeKey: $"trial-{sub.Id}-{daysLeft}");
            }
        }

        // Past-due dunning escalation
        var pastDue = await db.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.PastDue && s.PastDueSince != null)
            .ToListAsync(ct);

        foreach (var sub in pastDue)
        {
            var daysOverdue = (int)(now - sub.PastDueSince!.Value).TotalDays;
            var user = await userManager.FindByIdAsync(sub.UserId);
            if (user == null) continue;

            if (daysOverdue == 7)
            {
                await notifications.NotifyAsync(user.Id, "Final payment warning",
                    "Your account is 7 days overdue. Please pay to avoid suspension.", NotificationType.Error,
                    dedupeKey: $"dunning7-{sub.Id}");
            }
            else if (daysOverdue >= 14)
            {
                await notifications.NotifyAsync(user.Id, "Data deletion scheduled",
                    "Your account is 14 days overdue. Data deletion has been scheduled. Pay now to prevent loss.",
                    NotificationType.Error, dedupeKey: $"dunning14-{sub.Id}");
                if (user.IsActive)
                {
                    await provisioning.SuspendAsync(user, "Account 14 days overdue — scheduled for data deletion.");
                }
            }
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Trial/dunning sweep complete: {Trials} trials, {PastDue} past-due", trials.Count, pastDue.Count);
    }
}
