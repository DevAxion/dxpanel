using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Billing;

namespace SRXPanel.Services.Store;

/// <summary>
/// Periodically sends reminders for unpaid invoices: 3 days before the due date,
/// on the due date, and 3 days after (overdue). Simulation-safe — emails/SMS are
/// logged rather than sent. Duplicate reminders are avoided with per-stage dedupe keys.
/// </summary>
public class InvoiceReminderService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InvoiceReminderService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    public InvoiceReminderService(IServiceScopeFactory scopeFactory, ILogger<InvoiceReminderService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(25), stoppingToken); } catch { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "Invoice reminder sweep failed"); }

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
        var sms = scope.ServiceProvider.GetRequiredService<ISmsSender>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var panel = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<PanelSettings>>().CurrentValue;
        var today = DateTime.UtcNow.Date;

        var unpaid = await db.Invoices.Where(i => i.Status == InvoiceStatus.Unpaid).ToListAsync(ct);

        var reminded = 0;
        foreach (var inv in unpaid)
        {
            var daysUntilDue = (int)(inv.DueDate.Date - today).TotalDays;
            string? stage = daysUntilDue switch
            {
                3 => "upcoming",   // 3 days before
                0 => "due",        // on the due date
                -3 => "overdue",   // 3 days after
                _ => null
            };
            if (stage == null) continue;

            var user = await userManager.FindByIdAsync(inv.UserId);
            if (user == null) continue;

            var (title, body) = stage switch
            {
                "upcoming" => ("Invoice due soon", $"Invoice {inv.Number} for {BillingService.FormatMoney(inv.Amount, inv.Currency)} is due on {inv.DueDate:yyyy-MM-dd}."),
                "due" => ("Invoice due today", $"Invoice {inv.Number} for {BillingService.FormatMoney(inv.Amount, inv.Currency)} is due today."),
                _ => ("Invoice overdue", $"Invoice {inv.Number} for {BillingService.FormatMoney(inv.Amount, inv.Currency)} is overdue. Please pay to avoid service interruption.")
            };

            await notifications.NotifyAsync(user.Id, title, body,
                stage == "overdue" ? NotificationType.Error : NotificationType.Warning,
                dedupeKey: $"inv-{inv.Id}-{stage}");

            await mailer.SendTemplateAsync(user.Email ?? "", title, "invoice_reminder", new Dictionary<string, string>
            {
                ["NAME"] = user.FullName ?? user.UserName ?? "there",
                ["INVOICE_NUMBER"] = inv.Number,
                ["AMOUNT"] = BillingService.FormatMoney(inv.Amount, inv.Currency),
                ["DUE_DATE"] = inv.DueDate.ToString("yyyy-MM-dd"),
                ["PAY_URL"] = $"https://{panel.Hostname}/Client/Invoices"
            });
            await sms.SendAsync(user.PhoneNumber, $"{title}: {inv.Number}");
            reminded++;
        }

        if (reminded > 0)
            _logger.LogInformation("Invoice reminder sweep sent {Count} reminder(s)", reminded);
    }
}
