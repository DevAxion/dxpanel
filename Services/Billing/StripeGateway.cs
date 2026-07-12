using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Stripe;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Interfaces;
using Plan = SRXPanel.Models.Plan;
using Coupon = SRXPanel.Models.Coupon;

namespace SRXPanel.Services.Billing;

public class StripeGateway : IStripeGateway
{
    private const string ServiceName = "stripe";
    private readonly ICommandRunner _log;
    private readonly StripeSettings _settings;
    private readonly ILogger<StripeGateway> _logger;

    public bool SimulationMode { get; }
    public string PublishableKey => _settings.PublishableKey;

    public StripeGateway(ICommandRunner log, IOptionsMonitor<StripeSettings> settings,
        IConfiguration config, ILogger<StripeGateway> logger)
    {
        _log = log;
        _settings = settings.CurrentValue;
        _logger = logger;

        var configured = config.GetValue<bool?>("SimulationMode");
        var simFlag = configured ?? !RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        // Also force simulation when no secret key is configured, regardless of platform.
        SimulationMode = simFlag || string.IsNullOrWhiteSpace(_settings.SecretKey);

        if (!SimulationMode)
        {
            StripeConfiguration.ApiKey = _settings.SecretKey;
        }
    }

    private static string FakeId(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..Math.Min(28, prefix.Length + 25)];
    private static RequestOptions Idempotent() => new() { IdempotencyKey = Guid.NewGuid().ToString() };

    public async Task<string> EnsureCustomerAsync(ApplicationUser user)
    {
        if (SimulationMode)
        {
            var id = FakeId("sim_cus");
            await _log.LogExternalAsync($"stripe.customers.create(email={user.Email})", $"customer {id}", true, ServiceName);
            return id;
        }

        var service = new CustomerService();
        var customer = await service.CreateAsync(new CustomerCreateOptions
        {
            Email = user.Email,
            Name = user.FullName,
            Metadata = new Dictionary<string, string> { ["userId"] = user.Id }
        }, Idempotent());
        await _log.LogExternalAsync($"stripe.customers.create(email={user.Email})", $"customer {customer.Id}", false, ServiceName);
        return customer.Id;
    }

    public async Task<(string productId, string priceId)> SyncPlanAsync(Plan plan)
    {
        var interval = plan.BillingCycle == BillingCycle.Annual ? "year" : "month";
        var amount = (long)(plan.Price * 100);

        if (SimulationMode)
        {
            var prod = plan.StripeProductId ?? FakeId("sim_prod");
            var price = FakeId("sim_price");
            await _log.LogExternalAsync(
                $"stripe.products.upsert(name={plan.Name}) + prices.create({amount} {plan.Currency}/{interval})",
                $"product {prod}, price {price}", true, ServiceName);
            return (prod, price);
        }

        var productService = new ProductService();
        Product product;
        if (!string.IsNullOrEmpty(plan.StripeProductId))
        {
            product = await productService.UpdateAsync(plan.StripeProductId,
                new ProductUpdateOptions { Name = plan.Name, Description = plan.Description });
        }
        else
        {
            product = await productService.CreateAsync(new ProductCreateOptions
            {
                Name = plan.Name,
                Description = string.IsNullOrEmpty(plan.Description) ? null : plan.Description
            }, Idempotent());
        }

        var priceService = new PriceService();
        var newPrice = await priceService.CreateAsync(new PriceCreateOptions
        {
            Product = product.Id,
            UnitAmount = amount,
            Currency = plan.Currency,
            Recurring = new PriceRecurringOptions { Interval = interval }
        }, Idempotent());

        await _log.LogExternalAsync($"stripe.plan.sync({plan.Name})", $"product {product.Id}, price {newPrice.Id}", false, ServiceName);
        return (product.Id, newPrice.Id);
    }

    public async Task<StripeSubscriptionResult> CreateSubscriptionAsync(ApplicationUser user, Plan plan,
        string customerId, string? couponCode, string? paymentMethodToken)
    {
        var now = DateTime.UtcNow;
        var periodEnd = plan.BillingCycle == BillingCycle.Annual ? now.AddYears(1) : now.AddMonths(1);

        if (SimulationMode)
        {
            var subId = FakeId("sim_sub");
            var invId = FakeId("sim_inv");
            await _log.LogExternalAsync(
                $"stripe.subscriptions.create(customer={customerId}, price={plan.StripePriceId ?? "sim_price"}, coupon={couponCode ?? "none"})",
                $"subscription {subId} status=active, invoice {invId} paid", true, ServiceName);
            return new StripeSubscriptionResult(subId, customerId, SubscriptionStatus.Active, now, periodEnd,
                invId, $"/Billing/InvoicePdf/{invId}", plan.Price, plan.Currency);
        }

        // Attach the payment method + set as default (if provided).
        if (!string.IsNullOrEmpty(paymentMethodToken))
        {
            await new PaymentMethodService().AttachAsync(paymentMethodToken,
                new PaymentMethodAttachOptions { Customer = customerId });
            await new CustomerService().UpdateAsync(customerId, new CustomerUpdateOptions
            {
                InvoiceSettings = new CustomerInvoiceSettingsOptions { DefaultPaymentMethod = paymentMethodToken }
            });
        }

        var options = new SubscriptionCreateOptions
        {
            Customer = customerId,
            Items = new List<SubscriptionItemOptions> { new() { Price = plan.StripePriceId } },
            Expand = new List<string> { "latest_invoice.payment_intent" }
        };
        if (!string.IsNullOrEmpty(couponCode))
        {
            options.Discounts = new List<SubscriptionDiscountOptions>
            {
                new() { Coupon = couponCode }
            };
        }

        var sub = await new SubscriptionService().CreateAsync(options, Idempotent());
        await _log.LogExternalAsync($"stripe.subscriptions.create(customer={customerId})", $"subscription {sub.Id} status={sub.Status}", false, ServiceName);

        // Period dates are computed from the billing cycle to stay compatible
        // across Stripe API versions (they moved off the top-level object).
        var status = MapStatus(sub.Status);
        return new StripeSubscriptionResult(sub.Id, customerId, status, now, periodEnd,
            sub.LatestInvoiceId, null, plan.Price, plan.Currency);
    }

    public async Task CancelSubscriptionAsync(string? stripeSubscriptionId)
    {
        if (string.IsNullOrEmpty(stripeSubscriptionId)) return;

        if (SimulationMode)
        {
            await _log.LogExternalAsync($"stripe.subscriptions.cancel({stripeSubscriptionId})", "cancelled", true, ServiceName);
            return;
        }

        await new SubscriptionService().CancelAsync(stripeSubscriptionId);
        await _log.LogExternalAsync($"stripe.subscriptions.cancel({stripeSubscriptionId})", "cancelled", false, ServiceName);
    }

    public async Task<StripePaymentMethodResult> AttachPaymentMethodAsync(string customerId, string paymentMethodToken)
    {
        if (SimulationMode)
        {
            var pmId = FakeId("sim_pm");
            var result = new StripePaymentMethodResult(pmId, "visa", "4242", 12, DateTime.UtcNow.Year + 3);
            await _log.LogExternalAsync($"stripe.paymentMethods.attach({pmId} -> {customerId})", "Visa ****4242", true, ServiceName);
            return result;
        }

        var pmService = new PaymentMethodService();
        var pm = await pmService.AttachAsync(paymentMethodToken, new PaymentMethodAttachOptions { Customer = customerId });
        await _log.LogExternalAsync($"stripe.paymentMethods.attach({pm.Id} -> {customerId})", $"{pm.Card?.Brand} ****{pm.Card?.Last4}", false, ServiceName);
        return new StripePaymentMethodResult(pm.Id, pm.Card?.Brand ?? "card", pm.Card?.Last4 ?? "0000",
            (int)(pm.Card?.ExpMonth ?? 0), (int)(pm.Card?.ExpYear ?? 0));
    }

    public async Task<StripeChargeResult> ChargeAsync(string customerId, decimal amount, string currency, string description, string? paymentMethodId = null)
    {
        if (amount <= 0)
        {
            // Nothing to charge (e.g. a fully-credited downgrade).
            return new StripeChargeResult(FakeId("sim_free"), true, amount, currency, null);
        }

        if (SimulationMode)
        {
            var chargeId = FakeId("sim_ch");
            await _log.LogExternalAsync(
                $"stripe.paymentIntents.create(customer={customerId}, amount={amount:0.00} {currency}, desc={description})",
                $"charge {chargeId} succeeded", true, ServiceName);
            return new StripeChargeResult(chargeId, true, amount, currency, $"/Client/Invoices");
        }

        var intent = await new PaymentIntentService().CreateAsync(new PaymentIntentCreateOptions
        {
            Customer = customerId,
            Amount = (long)(amount * 100),
            Currency = currency,
            Description = description,
            PaymentMethod = paymentMethodId,
            Confirm = paymentMethodId != null,
            OffSession = paymentMethodId != null,
            AutomaticPaymentMethods = paymentMethodId == null
                ? new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true } : null
        }, Idempotent());

        await _log.LogExternalAsync($"stripe.paymentIntents.create(customer={customerId}, amount={amount:0.00})",
            $"intent {intent.Id} status={intent.Status}", false, ServiceName);
        return new StripeChargeResult(intent.Id, intent.Status == "succeeded", amount, currency, null);
    }

    public async Task DetachPaymentMethodAsync(string stripePaymentMethodId)
    {
        if (SimulationMode)
        {
            await _log.LogExternalAsync($"stripe.paymentMethods.detach({stripePaymentMethodId})", "detached", true, ServiceName);
            return;
        }
        await new PaymentMethodService().DetachAsync(stripePaymentMethodId);
        await _log.LogExternalAsync($"stripe.paymentMethods.detach({stripePaymentMethodId})", "detached", false, ServiceName);
    }

    public async Task SetDefaultPaymentMethodAsync(string customerId, string stripePaymentMethodId)
    {
        if (SimulationMode)
        {
            await _log.LogExternalAsync($"stripe.customers.setDefault({customerId} -> {stripePaymentMethodId})", "default set", true, ServiceName);
            return;
        }
        await new CustomerService().UpdateAsync(customerId, new CustomerUpdateOptions
        {
            InvoiceSettings = new CustomerInvoiceSettingsOptions { DefaultPaymentMethod = stripePaymentMethodId }
        });
        await _log.LogExternalAsync($"stripe.customers.setDefault({customerId})", "default set", false, ServiceName);
    }

    public async Task<string> SyncCouponAsync(Coupon coupon)
    {
        if (SimulationMode)
        {
            var id = coupon.StripeCouponId ?? FakeId("sim_coup");
            await _log.LogExternalAsync($"stripe.coupons.create(code={coupon.Code}, {coupon.DiscountPercent}%)", $"coupon {id}", true, ServiceName);
            return id;
        }

        var service = new CouponService();
        var created = await service.CreateAsync(new CouponCreateOptions
        {
            Id = coupon.Code,
            PercentOff = coupon.DiscountPercent,
            Duration = "once",
            MaxRedemptions = coupon.MaxUses > 0 ? coupon.MaxUses : null
        }, Idempotent());
        await _log.LogExternalAsync($"stripe.coupons.create(code={coupon.Code})", $"coupon {created.Id}", false, ServiceName);
        return created.Id;
    }

    public StripeWebhookEvent? ConstructWebhookEvent(string json, string? signatureHeader)
    {
        if (SimulationMode || string.IsNullOrEmpty(_settings.WebhookSecret))
        {
            // In simulation, trust the payload (used by the local webhook tester).
            try
            {
                using var doc = JsonDocument.Parse(json);
                var type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                var data = new Dictionary<string, string>();
                if (doc.RootElement.TryGetProperty("data", out var d) &&
                    d.TryGetProperty("object", out var obj))
                {
                    foreach (var prop in obj.EnumerateObject())
                    {
                        data[prop.Name] = prop.Value.ToString();
                    }
                }
                return new StripeWebhookEvent(type, json, data);
            }
            catch
            {
                return null;
            }
        }

        try
        {
            var stripeEvent = EventUtility.ConstructEvent(json, signatureHeader, _settings.WebhookSecret);
            return new StripeWebhookEvent(stripeEvent.Type, json, new Dictionary<string, string>());
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe webhook signature verification failed");
            return null;
        }
    }

    public string CustomerDashboardUrl(string? customerId) =>
        string.IsNullOrEmpty(customerId) ? "#" : $"https://dashboard.stripe.com/customers/{customerId}";

    private static SubscriptionStatus MapStatus(string stripeStatus) => stripeStatus switch
    {
        "active" => SubscriptionStatus.Active,
        "trialing" => SubscriptionStatus.Trialing,
        "past_due" or "unpaid" or "incomplete" => SubscriptionStatus.PastDue,
        "canceled" => SubscriptionStatus.Cancelled,
        _ => SubscriptionStatus.Active
    };
}
