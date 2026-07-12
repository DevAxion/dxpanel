using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;

namespace SRXPanel.Services.Billing;

public interface IBillingService
{
    Task<Subscription> StartTrialAsync(ApplicationUser user, Plan plan);
    Task<Subscription> CheckoutAsync(ApplicationUser user, Plan plan, string? paymentMethodToken, string? couponCode);
    Task CancelAsync(Subscription subscription);
    Task<PaymentMethod> AddPaymentMethodAsync(ApplicationUser user, string? token);
    Task SetDefaultPaymentMethodAsync(ApplicationUser user, int paymentMethodId);
    Task RemovePaymentMethodAsync(ApplicationUser user, int paymentMethodId);

    // Webhook-driven
    Task HandlePaymentSucceededAsync(string? stripeSubscriptionId);
    Task HandlePaymentFailedAsync(string? stripeSubscriptionId);
    Task HandleSubscriptionDeletedAsync(string? stripeSubscriptionId);
    Task HandleSubscriptionUpdatedAsync(string? stripeSubscriptionId, int? newPlanId);

    Coupon? ValidateCoupon(string? code);
    decimal ApplyDiscount(decimal amount, Coupon? coupon);
}

public class BillingService : IBillingService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IStripeGateway _stripe;
    private readonly IProvisioningService _provisioning;
    private readonly IMailerService _mailer;
    private readonly INotificationService _notifications;
    private readonly PanelSettings _panel;

    public BillingService(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IStripeGateway stripe,
        IProvisioningService provisioning, IMailerService mailer, INotificationService notifications,
        IOptionsMonitor<PanelSettings> panel)
    {
        _db = db;
        _userManager = userManager;
        _stripe = stripe;
        _provisioning = provisioning;
        _mailer = mailer;
        _notifications = notifications;
        _panel = panel.CurrentValue;
    }

    public Coupon? ValidateCoupon(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var coupon = _db.Coupons.FirstOrDefault(c => c.Code == code.Trim().ToUpperInvariant());
        return coupon is { IsValid: true } ? coupon : null;
    }

    public decimal ApplyDiscount(decimal amount, Coupon? coupon) =>
        coupon == null ? amount : Math.Round(amount * (1 - coupon.DiscountPercent / 100m), 2);

    public async Task<Subscription> StartTrialAsync(ApplicationUser user, Plan plan)
    {
        var customerId = await _stripe.EnsureCustomerAsync(user);
        var now = DateTime.UtcNow;
        var trialEnd = now.AddDays(14);

        var sub = new Subscription
        {
            UserId = user.Id,
            PlanId = plan.Id,
            Status = SubscriptionStatus.Trialing,
            StripeCustomerId = customerId,
            CurrentPeriodStart = now,
            CurrentPeriodEnd = trialEnd,
            TrialEndsAt = trialEnd,
            CreatedAt = now
        };
        _db.Subscriptions.Add(sub);
        await _db.SaveChangesAsync();

        await _provisioning.ProvisionAsync(user, plan);
        return sub;
    }

    public async Task<Subscription> CheckoutAsync(ApplicationUser user, Plan plan, string? paymentMethodToken, string? couponCode)
    {
        var coupon = ValidateCoupon(couponCode);
        var customerId = await _stripe.EnsureCustomerAsync(user);

        var result = await _stripe.CreateSubscriptionAsync(user, plan, customerId, coupon?.StripeCouponId ?? coupon?.Code, paymentMethodToken);
        var amount = ApplyDiscount(plan.BillingCycle == BillingCycle.Annual ? plan.AnnualPrice : plan.Price, coupon);

        var sub = new Subscription
        {
            UserId = user.Id,
            PlanId = plan.Id,
            Status = result.Status,
            StripeSubscriptionId = result.SubscriptionId,
            StripeCustomerId = customerId,
            CurrentPeriodStart = result.CurrentPeriodStart,
            CurrentPeriodEnd = result.CurrentPeriodEnd,
            CouponId = coupon?.Id,
            CreatedAt = DateTime.UtcNow
        };
        _db.Subscriptions.Add(sub);
        await _db.SaveChangesAsync();

        // Record the paid invoice
        var invoice = new Invoice
        {
            UserId = user.Id,
            SubscriptionId = sub.Id,
            Amount = amount,
            Currency = plan.Currency,
            Status = InvoiceStatus.Paid,
            StripeInvoiceId = result.InvoiceId,
            PaidAt = DateTime.UtcNow,
            DueDate = DateTime.UtcNow,
            Number = $"INV-{DateTime.UtcNow:yyyyMMdd}-{sub.Id:D4}",
            InvoicePdf = result.InvoicePdf,
            CreatedAt = DateTime.UtcNow
        };
        _db.Invoices.Add(invoice);

        // Store the payment method metadata (simulation returns a fake Visa ****4242)
        if (!string.IsNullOrEmpty(paymentMethodToken) || _stripe.SimulationMode)
        {
            var pm = await _stripe.AttachPaymentMethodAsync(customerId, paymentMethodToken ?? "sim_tok");
            _db.PaymentMethods.Add(new PaymentMethod
            {
                UserId = user.Id,
                StripePaymentMethodId = pm.PaymentMethodId,
                Brand = pm.Brand,
                Last4 = pm.Last4,
                ExpMonth = pm.ExpMonth,
                ExpYear = pm.ExpYear,
                IsDefault = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (coupon != null)
        {
            coupon.UsedCount++;
        }

        await _db.SaveChangesAsync();

        // Provision + receipt email
        await _provisioning.ProvisionAsync(user, plan);
        await SendReceiptAsync(user, plan, invoice, sub);

        return sub;
    }

    public async Task CancelAsync(Subscription subscription)
    {
        await _stripe.CancelSubscriptionAsync(subscription.StripeSubscriptionId);
        subscription.Status = SubscriptionStatus.Cancelled;
        subscription.CancelledAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var user = await _userManager.FindByIdAsync(subscription.UserId);
        var plan = await _db.Plans.FindAsync(subscription.PlanId);
        if (user != null)
        {
            await _mailer.SendTemplateAsync(user.Email ?? "", "Your subscription has been cancelled", "cancellation",
                new Dictionary<string, string>
                {
                    ["NAME"] = user.FullName ?? user.UserName ?? "there",
                    ["PLAN"] = plan?.Name ?? "your plan",
                    ["PERIOD_END"] = subscription.CurrentPeriodEnd.ToString("yyyy-MM-dd"),
                    ["PRICING_URL"] = $"https://{_panel.Hostname}/Pricing"
                });
            await _notifications.NotifyAsync(user.Id, "Subscription cancelled",
                $"Your subscription is cancelled. Service remains active until {subscription.CurrentPeriodEnd:yyyy-MM-dd}.",
                NotificationType.Warning);
        }
    }

    public async Task<PaymentMethod> AddPaymentMethodAsync(ApplicationUser user, string? token)
    {
        var sub = await _db.Subscriptions.Where(s => s.UserId == user.Id)
            .OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync();
        var customerId = sub?.StripeCustomerId ?? await _stripe.EnsureCustomerAsync(user);

        var pm = await _stripe.AttachPaymentMethodAsync(customerId, token ?? "sim_tok");

        var hadDefault = await _db.PaymentMethods.AnyAsync(p => p.UserId == user.Id && p.IsDefault);
        var entity = new PaymentMethod
        {
            UserId = user.Id,
            StripePaymentMethodId = pm.PaymentMethodId,
            Brand = pm.Brand,
            Last4 = pm.Last4,
            ExpMonth = pm.ExpMonth,
            ExpYear = pm.ExpYear,
            IsDefault = !hadDefault,
            CreatedAt = DateTime.UtcNow
        };
        _db.PaymentMethods.Add(entity);
        await _db.SaveChangesAsync();
        return entity;
    }

    public async Task SetDefaultPaymentMethodAsync(ApplicationUser user, int paymentMethodId)
    {
        var methods = await _db.PaymentMethods.Where(p => p.UserId == user.Id).ToListAsync();
        var target = methods.FirstOrDefault(p => p.Id == paymentMethodId);
        if (target == null) return;

        var sub = await _db.Subscriptions.Where(s => s.UserId == user.Id)
            .OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync();
        if (sub?.StripeCustomerId != null)
        {
            await _stripe.SetDefaultPaymentMethodAsync(sub.StripeCustomerId, target.StripePaymentMethodId);
        }

        foreach (var m in methods) m.IsDefault = m.Id == paymentMethodId;
        await _db.SaveChangesAsync();
    }

    public async Task RemovePaymentMethodAsync(ApplicationUser user, int paymentMethodId)
    {
        var pm = await _db.PaymentMethods.FirstOrDefaultAsync(p => p.Id == paymentMethodId && p.UserId == user.Id);
        if (pm == null) return;

        await _stripe.DetachPaymentMethodAsync(pm.StripePaymentMethodId);
        _db.PaymentMethods.Remove(pm);
        await _db.SaveChangesAsync();
    }

    // ---- Webhook handlers ----

    public async Task HandlePaymentSucceededAsync(string? stripeSubscriptionId)
    {
        var sub = await FindSubAsync(stripeSubscriptionId);
        if (sub == null) return;

        var plan = await _db.Plans.FindAsync(sub.PlanId);
        var wasPastDue = sub.Status == SubscriptionStatus.PastDue;
        sub.Status = SubscriptionStatus.Active;
        sub.PastDueSince = null;
        sub.CurrentPeriodStart = DateTime.UtcNow;
        sub.CurrentPeriodEnd = plan?.BillingCycle == BillingCycle.Annual
            ? DateTime.UtcNow.AddYears(1) : DateTime.UtcNow.AddMonths(1);

        var invoice = new Invoice
        {
            UserId = sub.UserId,
            SubscriptionId = sub.Id,
            Amount = plan?.Price ?? 0,
            Currency = plan?.Currency ?? "usd",
            Status = InvoiceStatus.Paid,
            PaidAt = DateTime.UtcNow,
            DueDate = DateTime.UtcNow,
            Number = $"INV-{DateTime.UtcNow:yyyyMMdd}-{sub.Id:D4}",
            CreatedAt = DateTime.UtcNow
        };
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        var user = await _userManager.FindByIdAsync(sub.UserId);
        if (user != null && plan != null)
        {
            if (wasPastDue) await _provisioning.ReactivateAsync(user);
            await SendReceiptAsync(user, plan, invoice, sub);
        }
    }

    public async Task HandlePaymentFailedAsync(string? stripeSubscriptionId)
    {
        var sub = await FindSubAsync(stripeSubscriptionId);
        if (sub == null) return;

        sub.Status = SubscriptionStatus.PastDue;
        sub.PastDueSince ??= DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var user = await _userManager.FindByIdAsync(sub.UserId);
        var plan = await _db.Plans.FindAsync(sub.PlanId);
        if (user != null)
        {
            await _mailer.SendTemplateAsync(user.Email ?? "", "Payment failed — action required", "payment_failed",
                new Dictionary<string, string>
                {
                    ["NAME"] = user.FullName ?? user.UserName ?? "there",
                    ["PLAN"] = plan?.Name ?? "your plan",
                    ["AMOUNT"] = FormatMoney(plan?.Price ?? 0, plan?.Currency ?? "usd"),
                    ["PAY_URL"] = $"https://{_panel.Hostname}/Billing"
                });
            await _notifications.NotifyAsync(user.Id, "Payment failed",
                "We couldn't process your payment. Please update your payment method.", NotificationType.Error);
        }
    }

    public async Task HandleSubscriptionDeletedAsync(string? stripeSubscriptionId)
    {
        var sub = await FindSubAsync(stripeSubscriptionId);
        if (sub == null) return;

        sub.Status = SubscriptionStatus.Cancelled;
        sub.CancelledAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var user = await _userManager.FindByIdAsync(sub.UserId);
        if (user != null)
        {
            await _provisioning.SuspendAsync(user, "Subscription ended.");
        }
    }

    public async Task HandleSubscriptionUpdatedAsync(string? stripeSubscriptionId, int? newPlanId)
    {
        var sub = await FindSubAsync(stripeSubscriptionId);
        if (sub == null) return;

        if (newPlanId is int planId)
        {
            var plan = await _db.Plans.FindAsync(planId);
            if (plan != null)
            {
                sub.PlanId = plan.Id;
                var user = await _userManager.FindByIdAsync(sub.UserId);
                if (user != null)
                {
                    user.DiskQuotaMB = plan.DiskQuotaMB;
                    user.BandwidthQuotaMB = plan.BandwidthQuotaMB;
                    await _userManager.UpdateAsync(user);
                }
            }
        }
        await _db.SaveChangesAsync();
    }

    private Task<Subscription?> FindSubAsync(string? stripeSubscriptionId) =>
        string.IsNullOrEmpty(stripeSubscriptionId)
            ? Task.FromResult<Subscription?>(null)
            : _db.Subscriptions.FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubscriptionId);

    private async Task SendReceiptAsync(ApplicationUser user, Plan plan, Invoice invoice, Subscription sub)
    {
        await _mailer.SendTemplateAsync(user.Email ?? "", $"Receipt {invoice.Number}", "invoice",
            new Dictionary<string, string>
            {
                ["NAME"] = user.FullName ?? user.UserName ?? "there",
                ["PLAN"] = plan.Name,
                ["INVOICE_NUMBER"] = invoice.Number,
                ["AMOUNT"] = FormatMoney(invoice.Amount, invoice.Currency),
                ["DATE"] = invoice.CreatedAt.ToString("yyyy-MM-dd"),
                ["PDF_URL"] = invoice.InvoicePdf ?? "#",
                ["NEXT_BILLING"] = sub.CurrentPeriodEnd.ToString("yyyy-MM-dd")
            });
    }

    public static string FormatMoney(decimal amount, string currency) =>
        currency.ToLowerInvariant() switch
        {
            "usd" => $"${amount:0.00}",
            "eur" => $"€{amount:0.00}",
            "gbp" => $"£{amount:0.00}",
            _ => $"{amount:0.00} {currency.ToUpperInvariant()}"
        };
}
