using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Billing;

public interface IResellerBillingService
{
    Task<ResellerBillingConfig> GetConfigAsync(string resellerId);
    Task SaveConfigAsync(ResellerBillingConfig config);
    Task<decimal> GetBalanceAsync(string resellerId);
    Task<ResellerTransaction> AddTransactionAsync(string resellerId, ResellerTransactionType type,
        decimal amount, string? description, string? referenceId = null);
    Task<List<ResellerTransaction>> GetTransactionsAsync(string resellerId,
        ResellerTransactionType? type = null, DateTime? from = null, DateTime? to = null);
    Task<ResellerInvoice> GenerateMonthlyInvoiceAsync(string resellerId, DateTime periodStart, DateTime periodEnd, decimal amount);
    Task<List<ResellerInvoice>> GetInvoicesAsync(string resellerId);
    Task AddCreditViaStripeAsync(string resellerId, decimal amount);
}

public class ResellerBillingService : IResellerBillingService
{
    private readonly ApplicationDbContext _db;
    private readonly ICommandRunner _log;

    public ResellerBillingService(ApplicationDbContext db, ICommandRunner log)
    {
        _db = db;
        _log = log;
    }

    public async Task<ResellerBillingConfig> GetConfigAsync(string resellerId)
    {
        var config = await _db.ResellerBillingConfigs.FirstOrDefaultAsync(c => c.ResellerId == resellerId);
        if (config == null)
        {
            config = new ResellerBillingConfig { ResellerId = resellerId };
            _db.ResellerBillingConfigs.Add(config);
            await _db.SaveChangesAsync();
        }
        return config;
    }

    public async Task SaveConfigAsync(ResellerBillingConfig config)
    {
        _db.ResellerBillingConfigs.Update(config);
        await _db.SaveChangesAsync();
    }

    public async Task<decimal> GetBalanceAsync(string resellerId)
    {
        var last = await _db.ResellerTransactions
            .Where(t => t.ResellerId == resellerId)
            .OrderByDescending(t => t.Id)
            .FirstOrDefaultAsync();
        return last?.Balance ?? 0m;
    }

    public async Task<ResellerTransaction> AddTransactionAsync(string resellerId, ResellerTransactionType type,
        decimal amount, string? description, string? referenceId = null)
    {
        var current = await GetBalanceAsync(resellerId);
        var delta = type == ResellerTransactionType.Credit ? amount : -amount;
        var tx = new ResellerTransaction
        {
            ResellerId = resellerId,
            Type = type,
            Amount = amount,
            Balance = current + delta,
            Description = description,
            ReferenceId = referenceId,
            CreatedAt = DateTime.UtcNow
        };
        _db.ResellerTransactions.Add(tx);
        await _db.SaveChangesAsync();
        return tx;
    }

    public Task<List<ResellerTransaction>> GetTransactionsAsync(string resellerId,
        ResellerTransactionType? type = null, DateTime? from = null, DateTime? to = null)
    {
        var q = _db.ResellerTransactions.Where(t => t.ResellerId == resellerId);
        if (type.HasValue) q = q.Where(t => t.Type == type.Value);
        if (from.HasValue) q = q.Where(t => t.CreatedAt >= from.Value);
        if (to.HasValue) q = q.Where(t => t.CreatedAt <= to.Value);
        return q.OrderByDescending(t => t.Id).ToListAsync();
    }

    public async Task<ResellerInvoice> GenerateMonthlyInvoiceAsync(string resellerId, DateTime periodStart, DateTime periodEnd, decimal amount)
    {
        var count = await _db.ResellerInvoices.CountAsync(i => i.ResellerId == resellerId);
        var invoice = new ResellerInvoice
        {
            ResellerId = resellerId,
            Number = $"RSL-{DateTime.UtcNow:yyyyMM}-{count + 1:0000}",
            Amount = amount,
            Status = ResellerInvoiceStatus.Unpaid,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            DueDate = DateTime.UtcNow.AddDays(14),
            CreatedAt = DateTime.UtcNow
        };
        _db.ResellerInvoices.Add(invoice);
        await _db.SaveChangesAsync();
        return invoice;
    }

    public Task<List<ResellerInvoice>> GetInvoicesAsync(string resellerId) =>
        _db.ResellerInvoices.Where(i => i.ResellerId == resellerId)
            .OrderByDescending(i => i.Id).ToListAsync();

    public async Task AddCreditViaStripeAsync(string resellerId, decimal amount)
    {
        // Simulation-safe: log the intended Stripe charge and credit the ledger.
        var refId = $"pi_{Guid.NewGuid():N}"[..20];
        await _log.LogExternalAsync(
            $"stripe.paymentIntents.create(amount={amount}, reseller={resellerId})",
            $"payment {refId} succeeded", true, "stripe");
        await AddTransactionAsync(resellerId, ResellerTransactionType.Credit, amount,
            "Account top-up via card", refId);
    }
}
