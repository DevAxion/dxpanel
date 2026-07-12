using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Billing;

namespace SRXPanel.Services.Store;

public enum ServiceKind { Shared, Vps, Reseller }

/// <summary>A unified "My Services" row covering shared hosting subscriptions and VPS/reseller services.</summary>
public record ServiceView(
    ServiceKind Kind,
    int Id,                       // subscription id (Shared) OR client service id (Vps/Reseller)
    string Name,
    SubscriptionStatus Status,
    DateTime Expiry,
    DateTime? TrialEndsAt,
    string ResourceSummary,
    int DomainsUsed,
    int MaxDomains,
    long DiskUsedMB,
    long DiskQuotaMB,
    int MaxEmails,
    int MaxDatabases,
    bool CanUpgrade,
    Plan? Plan)
{
    // Convenience accessor used by the dashboard widget.
    public int SubscriptionId => Kind == ServiceKind.Shared ? Id : 0;
}

public record UpgradeQuote(Plan Current, Plan Target, bool IsUpgrade, decimal PriceDifference,
    decimal ProratedAmount, int DaysRemaining, int PeriodDays);

public interface IStoreService
{
    Task<List<ServiceView>> GetActiveServicesAsync(string userId);
    Task<List<ClientAddon>> GetActiveAddonsAsync(string userId);
    Task<List<DomainRegistration>> GetDomainRegistrationsAsync(string userId);
    Task<PaymentMethod?> GetDefaultCardAsync(string userId);

    Task<(bool ok, string message, int? subscriptionId)> OrderPlanAsync(ApplicationUser user, int planId);
    Task<(bool ok, string message)> CancelServiceAsync(ApplicationUser user, int subscriptionId);
    Task<(bool ok, string message)> CancelClientServiceAsync(ApplicationUser user, int clientServiceId);

    Task<UpgradeQuote?> QuoteUpgradeAsync(int subscriptionId, int newPlanId);
    Task<(bool ok, string message)> ApplyPlanChangeAsync(ApplicationUser user, int subscriptionId, int newPlanId);

    Task<(bool ok, string message)> PurchaseAddonAsync(ApplicationUser user, int addonId, int quantity);
    Task<(bool ok, string message)> RegisterDomainAsync(ApplicationUser user, string domainName, decimal price);

    // Cart
    Task AddToCartAsync(string userId, CartItemType type, int referenceId, string label, decimal price, string? domainName = null, int quantity = 1);
    Task<List<CartItem>> GetCartAsync(string userId);
    Task<int> CartCountAsync(string userId);
    Task RemoveFromCartAsync(string userId, int cartItemId);
    Task<(bool ok, string message)> CheckoutCartAsync(ApplicationUser user, string? couponCode);

    // Invoices
    Task<(bool ok, string message)> PayInvoiceAsync(ApplicationUser user, int invoiceId);
    Task RecomputeQuotasAsync(ApplicationUser user);
}

public class StoreService : IStoreService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IStripeGateway _stripe;
    private readonly IBillingService _billing;
    private readonly IProvisioningService _provisioning;
    private readonly IMailerService _mailer;
    private readonly INotificationService _notifications;
    private readonly ISmsSender _sms;
    private readonly IFileManagerService _files;
    private readonly PanelSettings _panel;
    private readonly ILogger<StoreService> _logger;

    public StoreService(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IStripeGateway stripe,
        IBillingService billing, IProvisioningService provisioning, IMailerService mailer,
        INotificationService notifications, ISmsSender sms, IFileManagerService files,
        IOptionsMonitor<PanelSettings> panel, ILogger<StoreService> logger)
    {
        _db = db;
        _userManager = userManager;
        _stripe = stripe;
        _billing = billing;
        _provisioning = provisioning;
        _mailer = mailer;
        _notifications = notifications;
        _sms = sms;
        _files = files;
        _panel = panel.CurrentValue;
        _logger = logger;
    }

    /// <summary>
    /// Returns every active hosting service for the user — shared-hosting subscriptions
    /// AND VPS/reseller services (ClientService rows). Both are filtered by UserId.
    /// </summary>
    public async Task<List<ServiceView>> GetActiveServicesAsync(string userId)
    {
        var domainsUsed = await _db.Domains.CountAsync(d => d.UserId == userId);
        var diskUsedMB = _files.GetUsedBytes(userId) / 1024 / 1024;

        // Shared hosting (Subscription)
        var subs = await _db.Subscriptions.Include(s => s.Plan)
            .Where(s => s.UserId == userId && s.Status != SubscriptionStatus.Cancelled)
            .OrderByDescending(s => s.CreatedAt).ToListAsync();

        var views = subs.Select(s => new ServiceView(
            ServiceKind.Shared, s.Id, s.Plan?.Name ?? "Hosting Plan", s.Status,
            s.CurrentPeriodEnd, s.TrialEndsAt,
            s.Plan == null ? "" : $"{Disk(s.Plan.DiskQuotaMB)} disk · {Limit(s.Plan.MaxDomains)} domains · {Limit(s.Plan.MaxEmails)} emails · {Limit(s.Plan.MaxDatabases)} DBs",
            domainsUsed, s.Plan?.MaxDomains ?? 0, diskUsedMB, s.Plan?.DiskQuotaMB ?? 0,
            s.Plan?.MaxEmails ?? 0, s.Plan?.MaxDatabases ?? 0, true, s.Plan)).ToList();

        // VPS + Reseller (ClientService)
        var services = await _db.ClientServices
            .Where(c => c.UserId == userId && c.Status != SubscriptionStatus.Cancelled)
            .OrderByDescending(c => c.CreatedAt).ToListAsync();

        views.AddRange(services.Select(c => new ServiceView(
            c.Type == ClientServiceType.Vps ? ServiceKind.Vps : ServiceKind.Reseller,
            c.Id, c.Name, c.Status, c.CurrentPeriodEnd, null,
            c.ResourceSummary, 0, 0, 0, 0, 0, 0, false, null)));

        return views;
    }

    public Task<List<ClientAddon>> GetActiveAddonsAsync(string userId) =>
        _db.ClientAddons.Include(a => a.Addon)
            .Where(a => a.UserId == userId && a.IsActive)
            .OrderByDescending(a => a.PurchasedAt).ToListAsync();

    public Task<List<DomainRegistration>> GetDomainRegistrationsAsync(string userId) =>
        _db.DomainRegistrations.Where(d => d.UserId == userId)
            .OrderByDescending(d => d.RegisteredAt).ToListAsync();

    private static string Disk(long mb) => mb >= 1024 ? $"{mb / 1024} GB" : $"{mb} MB";
    private static string Limit(int n) => n == 0 ? "Unlimited" : n.ToString();

    public Task<PaymentMethod?> GetDefaultCardAsync(string userId) =>
        _db.PaymentMethods.Where(p => p.UserId == userId && p.IsDefault)
            .OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync();

    // ---------------- Order a new plan ----------------
    public async Task<(bool ok, string message, int? subscriptionId)> OrderPlanAsync(ApplicationUser user, int planId)
    {
        var plan = await _db.Plans.FindAsync(planId);
        if (plan == null || !plan.IsActive) return (false, "Plan not found.", null);

        // Reuse the billing pipeline: charges the saved card, provisions, emails a receipt.
        var sub = await _billing.CheckoutAsync(user, plan, null, null);
        await _sms.SendAsync(user.PhoneNumber, $"Your {plan.Name} hosting is now active.");
        return (true, $"{plan.Name} ordered and provisioned.", sub.Id);
    }

    public async Task<(bool ok, string message)> CancelServiceAsync(ApplicationUser user, int subscriptionId)
    {
        var sub = await _db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId && s.UserId == user.Id);
        if (sub == null) return (false, "Service not found.");
        await _billing.CancelAsync(sub);
        return (true, "Service cancelled. It stays active until the end of the current period.");
    }

    public async Task<(bool ok, string message)> CancelClientServiceAsync(ApplicationUser user, int clientServiceId)
    {
        var svc = await _db.ClientServices.FirstOrDefaultAsync(c => c.Id == clientServiceId && c.UserId == user.Id);
        if (svc == null) return (false, "Service not found.");
        svc.Status = SubscriptionStatus.Cancelled;
        svc.CancelledAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _notifications.NotifyAsync(user.Id, "Service cancelled",
            $"{svc.Name} has been cancelled. It stays active until {svc.CurrentPeriodEnd:MMM d, yyyy}.", NotificationType.Warning);
        return (true, "Service cancelled. It stays active until the end of the current period.");
    }

    /// <summary>Creates a ClientService record for a VPS or reseller order (charge handled by the caller).</summary>
    private async Task<ClientService> CreateClientServiceAsync(ApplicationUser user, ClientServiceType type, int referenceId)
    {
        string name; string summary; decimal price; string currency = "usd"; BillingCycle cycle = BillingCycle.Monthly;

        if (type == ClientServiceType.Vps)
        {
            var vps = await _db.VpsPlans.FindAsync(referenceId);
            name = vps?.Name ?? "VPS Plan";
            price = vps?.Price ?? 0;
            cycle = vps?.BillingCycle ?? BillingCycle.Monthly;
            summary = vps == null ? "" : $"{vps.CpuCores} vCPU · {vps.RamMB / 1024} GB RAM · {vps.DiskGB} GB SSD · {vps.Location}";
        }
        else
        {
            var pkg = await _db.ResellerPackages.FindAsync(referenceId);
            name = pkg?.Name ?? "Reseller Package";
            price = pkg?.Price ?? 0;
            cycle = pkg?.BillingCycle ?? BillingCycle.Monthly;
            summary = pkg == null ? "" : $"{Disk(pkg.DiskQuotaMB)} disk · {Limit(pkg.MaxDomains)} domains · white-label";
        }

        var now = DateTime.UtcNow;
        var svc = new ClientService
        {
            UserId = user.Id, Type = type, ReferenceId = referenceId, Name = name,
            ResourceSummary = summary, Price = price, Currency = currency, BillingCycle = cycle,
            Status = SubscriptionStatus.Active, CurrentPeriodStart = now,
            CurrentPeriodEnd = cycle == BillingCycle.Annual ? now.AddYears(1) : now.AddMonths(1), CreatedAt = now
        };
        _db.ClientServices.Add(svc);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Created ClientService #{Id} ({Type} '{Name}') for user {User}", svc.Id, type, name, user.Id);

        await _notifications.NotifyAsync(user.Id, "Service activated", $"Your {name} service is now active.", NotificationType.Success);
        await _sms.SendAsync(user.PhoneNumber, $"Your {name} service is now active.");
        return svc;
    }

    // ---------------- Upgrade / downgrade ----------------
    public async Task<UpgradeQuote?> QuoteUpgradeAsync(int subscriptionId, int newPlanId)
    {
        var sub = await _db.Subscriptions.Include(s => s.Plan).FirstOrDefaultAsync(s => s.Id == subscriptionId);
        var target = await _db.Plans.FindAsync(newPlanId);
        if (sub?.Plan == null || target == null) return null;

        var periodDays = Math.Max(1, (int)(sub.CurrentPeriodEnd - sub.CurrentPeriodStart).TotalDays);
        var daysRemaining = Math.Max(0, (int)(sub.CurrentPeriodEnd - DateTime.UtcNow).TotalDays);
        var diff = target.Price - sub.Plan.Price;
        var prorated = Math.Round(Math.Abs(diff) * daysRemaining / periodDays, 2);

        return new UpgradeQuote(sub.Plan, target, diff >= 0, diff, prorated, daysRemaining, periodDays);
    }

    public async Task<(bool ok, string message)> ApplyPlanChangeAsync(ApplicationUser user, int subscriptionId, int newPlanId)
    {
        var quote = await QuoteUpgradeAsync(subscriptionId, newPlanId);
        var sub = await _db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId && s.UserId == user.Id);
        var target = await _db.Plans.FindAsync(newPlanId);
        if (quote == null || sub == null || target == null) return (false, "Invalid upgrade request.");

        if (quote.IsUpgrade && quote.ProratedAmount > 0)
        {
            var customerId = sub.StripeCustomerId ?? await _stripe.EnsureCustomerAsync(user);
            var card = await GetDefaultCardAsync(user.Id);
            var charge = await _stripe.ChargeAsync(customerId, quote.ProratedAmount, target.Currency,
                $"Upgrade to {target.Name} (prorated)", card?.StripePaymentMethodId);
            if (!charge.Success) return (false, "Payment failed. Please update your payment method.");

            await CreateInvoiceAsync(user.Id, sub.Id, quote.ProratedAmount, target.Currency, InvoiceStatus.Paid,
                $"Upgrade to {target.Name} (prorated)");
        }
        else if (!quote.IsUpgrade && quote.ProratedAmount > 0)
        {
            // Downgrade: apply a credit to the account (recorded as a negative paid invoice).
            await CreateInvoiceAsync(user.Id, sub.Id, -quote.ProratedAmount, target.Currency, InvoiceStatus.Paid,
                $"Credit for downgrade to {target.Name}");
        }

        sub.PlanId = target.Id;
        await _db.SaveChangesAsync();

        await _provisioning.ApplyPlanChangeAsync(user, target);
        await RecomputeQuotasAsync(user); // fold any add-ons back in on top of the new plan
        await _sms.SendAsync(user.PhoneNumber, $"Your plan changed to {target.Name}.");

        return (true, quote.IsUpgrade
            ? $"Upgraded to {target.Name}. New limits are active now."
            : $"Downgraded to {target.Name}. A credit was applied to your next invoice.");
    }

    // ---------------- Add-ons ----------------
    public async Task<(bool ok, string message)> PurchaseAddonAsync(ApplicationUser user, int addonId, int quantity)
    {
        var addon = await _db.Addons.FindAsync(addonId);
        if (addon == null || !addon.IsActive) return (false, "Add-on not found.");
        quantity = Math.Clamp(quantity, 1, 100);

        var total = addon.Price * quantity;
        var customerId = await _stripe.EnsureCustomerAsync(user);
        var card = await GetDefaultCardAsync(user.Id);
        var charge = await _stripe.ChargeAsync(customerId, total, addon.Currency, $"{quantity}× {addon.Name}", card?.StripePaymentMethodId);
        if (!charge.Success) return (false, "Payment failed.");

        _db.ClientAddons.Add(new ClientAddon
        {
            UserId = user.Id,
            AddonId = addon.Id,
            Quantity = quantity,
            PurchasedAt = DateTime.UtcNow,
            ExpiresAt = addon.BillingCycle == BillingCycle.Annual ? DateTime.UtcNow.AddYears(1) : DateTime.UtcNow.AddMonths(1),
            IsActive = true
        });
        await CreateInvoiceAsync(user.Id, null, total, addon.Currency, InvoiceStatus.Paid, $"{quantity}× {addon.Name}");
        await _db.SaveChangesAsync();

        // Apply resource effects (disk/bandwidth) to the live account.
        await RecomputeQuotasAsync(user);

        await _notifications.NotifyAsync(user.Id, "Add-on activated", $"{quantity}× {addon.Name} is now active on your account.", NotificationType.Success);
        await _mailer.SendTemplateAsync(user.Email ?? "", $"Receipt — {addon.Name}", "invoice", new Dictionary<string, string>
        {
            ["NAME"] = user.FullName ?? user.UserName ?? "there",
            ["PLAN"] = addon.Name,
            ["INVOICE_NUMBER"] = "-",
            ["AMOUNT"] = BillingService.FormatMoney(total, addon.Currency),
            ["DATE"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            ["PDF_URL"] = "/Client/Invoices",
            ["NEXT_BILLING"] = "-"
        });
        return (true, $"{addon.Name} activated.");
    }

    // ---------------- Domain registration ----------------
    public async Task<(bool ok, string message)> RegisterDomainAsync(ApplicationUser user, string domainName, decimal price)
    {
        domainName = domainName.Trim().ToLowerInvariant();
        if (await _db.Domains.AnyAsync(d => d.DomainName == domainName))
            return (false, "That domain already exists in the panel.");

        var customerId = await _stripe.EnsureCustomerAsync(user);
        var card = await GetDefaultCardAsync(user.Id);
        var charge = await _stripe.ChargeAsync(customerId, price, "usd", $"Domain registration: {domainName}", card?.StripePaymentMethodId);
        if (!charge.Success) return (false, "Payment failed.");

        var tld = domainName.Contains('.') ? domainName[(domainName.LastIndexOf('.'))..] : "";
        var reg = new DomainRegistration
        {
            UserId = user.Id, DomainName = domainName, Tld = tld, Price = price, Currency = "usd",
            Registrar = "Simulated Registrar", Status = DomainRegistrationStatus.Active,
            RegisteredAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddYears(1)
        };
        _db.DomainRegistrations.Add(reg);

        // Add the domain to the account + auto-create a DNS zone.
        var prefix = HostingHelpers.UserPrefix(user.UserName ?? user.Email ?? "user");
        var domain = new Domain
        {
            UserId = user.Id, DomainName = domainName,
            DocumentRoot = $"/home/{prefix}/public_html/{domainName}",
            IsActive = true, PhpVersion = _panel.DefaultPhpVersion, CreatedAt = DateTime.UtcNow
        };
        _db.Domains.Add(domain);
        await _db.SaveChangesAsync();

        _db.DnsZones.Add(new DnsZone { DomainId = domain.Id, UserId = user.Id, IsActive = true, CreatedAt = DateTime.UtcNow });
        await CreateInvoiceAsync(user.Id, null, price, "usd", InvoiceStatus.Paid, $"Domain registration: {domainName}");
        await _db.SaveChangesAsync();

        await _notifications.NotifyAsync(user.Id, "Domain registered", $"{domainName} is registered and a DNS zone was created.", NotificationType.Success);
        await _sms.SendAsync(user.PhoneNumber, $"Domain {domainName} registered successfully.");
        return (true, $"{domainName} registered and added to your account.");
    }

    // ---------------- Cart ----------------
    public async Task AddToCartAsync(string userId, CartItemType type, int referenceId, string label, decimal price, string? domainName = null, int quantity = 1)
    {
        _db.CartItems.Add(new CartItem
        {
            UserId = userId, Type = type, ReferenceId = referenceId, DomainName = domainName,
            Label = label, Price = price, Quantity = Math.Clamp(quantity, 1, 100), CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    public Task<List<CartItem>> GetCartAsync(string userId) =>
        _db.CartItems.Where(c => c.UserId == userId).OrderBy(c => c.CreatedAt).ToListAsync();

    public Task<int> CartCountAsync(string userId) => _db.CartItems.CountAsync(c => c.UserId == userId);

    public async Task RemoveFromCartAsync(string userId, int cartItemId)
    {
        var item = await _db.CartItems.FirstOrDefaultAsync(c => c.Id == cartItemId && c.UserId == userId);
        if (item != null) { _db.CartItems.Remove(item); await _db.SaveChangesAsync(); }
    }

    public async Task<(bool ok, string message)> CheckoutCartAsync(ApplicationUser user, string? couponCode)
    {
        var items = await GetCartAsync(user.Id);
        if (items.Count == 0) return (false, "Your cart is empty.");

        var coupon = _billing.ValidateCoupon(couponCode);
        var subtotal = items.Sum(i => i.LineTotal);
        var total = _billing.ApplyDiscount(subtotal, coupon);

        var customerId = await _stripe.EnsureCustomerAsync(user);
        var card = await GetDefaultCardAsync(user.Id);
        var charge = await _stripe.ChargeAsync(customerId, total, "usd", $"Cart checkout ({items.Count} item(s))", card?.StripePaymentMethodId);
        if (!charge.Success) return (false, "Payment failed. Please add a payment method and try again.");

        // Provision each item — every branch persists a record so the service shows in "My Services".
        var provisioned = 0;
        foreach (var item in items)
        {
            switch (item.Type)
            {
                case CartItemType.Plan:
                    var plan = await _db.Plans.FindAsync(item.ReferenceId);
                    if (plan != null)
                    {
                        var sub = await _billing.CheckoutAsync(user, plan, null, null);
                        _logger.LogInformation("Checkout: created Subscription #{Id} (plan '{Plan}') for user {User}", sub.Id, plan.Name, user.Id);
                        provisioned++;
                    }
                    break;
                case CartItemType.Vps:
                    await CreateClientServiceAsync(user, ClientServiceType.Vps, item.ReferenceId);
                    provisioned++;
                    break;
                case CartItemType.Reseller:
                    await CreateClientServiceAsync(user, ClientServiceType.Reseller, item.ReferenceId);
                    provisioned++;
                    break;
                case CartItemType.Addon:
                    await PurchaseAddonAsync(user, item.ReferenceId, item.Quantity);
                    provisioned++;
                    break;
                case CartItemType.Domain:
                    if (!string.IsNullOrEmpty(item.DomainName))
                    {
                        await RegisterDomainAsync(user, item.DomainName, item.Price);
                        provisioned++;
                    }
                    break;
            }
        }

        await CreateInvoiceAsync(user.Id, null, total, "usd", InvoiceStatus.Paid, $"Cart checkout ({items.Count} item(s))");
        if (coupon != null) coupon.UsedCount++;
        _db.CartItems.RemoveRange(items);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Cart checkout complete for user {User}: {Provisioned}/{Total} item(s) provisioned", user.Id, provisioned, items.Count);
        await _notifications.NotifyAsync(user.Id, "Order complete", "All items in your order have been provisioned.", NotificationType.Success);
        return (true, "Payment successful — your order has been provisioned.");
    }

    // ---------------- Invoices ----------------
    public async Task<(bool ok, string message)> PayInvoiceAsync(ApplicationUser user, int invoiceId)
    {
        var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId && i.UserId == user.Id);
        if (invoice == null) return (false, "Invoice not found.");
        if (invoice.Status == InvoiceStatus.Paid) return (false, "This invoice is already paid.");

        var customerId = await _stripe.EnsureCustomerAsync(user);
        var card = await GetDefaultCardAsync(user.Id);
        var charge = await _stripe.ChargeAsync(customerId, invoice.Amount, invoice.Currency, $"Invoice {invoice.Number}", card?.StripePaymentMethodId);
        if (!charge.Success) return (false, "Payment failed.");

        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _notifications.NotifyAsync(user.Id, "Invoice paid", $"Invoice {invoice.Number} has been paid. Thank you!", NotificationType.Success);
        return (true, $"Invoice {invoice.Number} paid.");
    }

    /// <summary>Effective quota = active plan quota + active add-on values, applied to the live account.</summary>
    public async Task RecomputeQuotasAsync(ApplicationUser user)
    {
        var sub = await _db.Subscriptions.Include(s => s.Plan)
            .Where(s => s.UserId == user.Id && s.Status != SubscriptionStatus.Cancelled)
            .OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync();
        var baseDisk = sub?.Plan?.DiskQuotaMB ?? user.DiskQuotaMB;
        var baseBw = sub?.Plan?.BandwidthQuotaMB ?? user.BandwidthQuotaMB;

        var addons = await _db.ClientAddons.Include(a => a.Addon)
            .Where(a => a.UserId == user.Id && a.IsActive).ToListAsync();
        var extraDisk = addons.Where(a => a.Addon!.Type == AddonType.ExtraDisk).Sum(a => a.Addon!.Value * a.Quantity);
        var extraBw = addons.Where(a => a.Addon!.Type == AddonType.ExtraBandwidth).Sum(a => a.Addon!.Value * a.Quantity);

        user.BandwidthQuotaMB = baseBw + extraBw;
        await _provisioning.ApplyDiskQuotaAsync(user, baseDisk + extraDisk);
        await _userManager.UpdateAsync(user);
    }

    private async Task CreateInvoiceAsync(string userId, int? subscriptionId, decimal amount, string currency, InvoiceStatus status, string description)
    {
        var count = await _db.Invoices.CountAsync() + 1;
        _db.Invoices.Add(new Invoice
        {
            UserId = userId,
            SubscriptionId = subscriptionId,
            Amount = amount,
            Currency = currency,
            Status = status,
            PaidAt = status == InvoiceStatus.Paid ? DateTime.UtcNow : null,
            DueDate = DateTime.UtcNow,
            Number = $"INV-{DateTime.UtcNow:yyyyMMdd}-{count:D4}",
            CreatedAt = DateTime.UtcNow
        });
    }
}
