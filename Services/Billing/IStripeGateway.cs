using SRXPanel.Models;

namespace SRXPanel.Services.Billing;

public record StripeSubscriptionResult(
    string SubscriptionId,
    string CustomerId,
    SubscriptionStatus Status,
    DateTime CurrentPeriodStart,
    DateTime CurrentPeriodEnd,
    string? InvoiceId,
    string? InvoicePdf,
    decimal Amount,
    string Currency);

public record StripePaymentMethodResult(
    string PaymentMethodId,
    string Brand,
    string Last4,
    int ExpMonth,
    int ExpYear);

public record StripeChargeResult(
    string ChargeId,
    bool Success,
    decimal Amount,
    string Currency,
    string? InvoicePdf);

public record StripeWebhookEvent(string Type, string Json, Dictionary<string, string> Data);

/// <summary>
/// Wraps all Stripe API interaction. When SimulationMode is on (Windows/dev or
/// no keys configured) it returns deterministic mock objects and logs the
/// would-be API call to the CommandLog instead of calling Stripe.
/// </summary>
public interface IStripeGateway
{
    bool SimulationMode { get; }
    string PublishableKey { get; }

    Task<string> EnsureCustomerAsync(ApplicationUser user);
    Task<(string productId, string priceId)> SyncPlanAsync(Plan plan);
    Task<StripeSubscriptionResult> CreateSubscriptionAsync(ApplicationUser user, Plan plan, string customerId, string? couponCode, string? paymentMethodToken);
    Task CancelSubscriptionAsync(string? stripeSubscriptionId);
    Task<StripePaymentMethodResult> AttachPaymentMethodAsync(string customerId, string paymentMethodToken);

    /// <summary>One-off charge (add-ons, upgrades, domains, cart checkout). Simulation-safe.</summary>
    Task<StripeChargeResult> ChargeAsync(string customerId, decimal amount, string currency, string description, string? paymentMethodId = null);
    Task DetachPaymentMethodAsync(string stripePaymentMethodId);
    Task SetDefaultPaymentMethodAsync(string customerId, string stripePaymentMethodId);
    Task<string> SyncCouponAsync(Coupon coupon);

    /// <summary>Verifies + parses a webhook payload. Returns null if signature invalid (real mode).</summary>
    StripeWebhookEvent? ConstructWebhookEvent(string json, string? signatureHeader);
    string CustomerDashboardUrl(string? customerId);
}
