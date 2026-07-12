using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Billing;

namespace SRXPanel.Pages.Admin;

public class ResellerBillingModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IResellerBillingService _billing;
    private readonly IAuditLogService _audit;

    public ResellerBillingModel(ApplicationDbContext db, IResellerBillingService billing, IAuditLogService audit)
    {
        _db = db;
        _billing = billing;
        _audit = audit;
    }

    public class Row
    {
        public ResellerProfile Profile { get; set; } = null!;
        public string UserName { get; set; } = string.Empty;
        public decimal Balance { get; set; }
        public Models.ResellerBillingModel Model { get; set; }
    }

    public List<Row> Rows { get; set; } = new();
    public decimal TotalFees { get; set; }
    public List<ResellerTransaction> RecentPayouts { get; set; } = new();

    private async Task LoadAsync()
    {
        var profiles = await _db.ResellerProfiles.Include(p => p.User).ToListAsync();
        foreach (var p in profiles)
        {
            var config = await _billing.GetConfigAsync(p.UserId);
            Rows.Add(new Row
            {
                Profile = p,
                UserName = p.User?.UserName ?? "—",
                Balance = await _billing.GetBalanceAsync(p.UserId),
                Model = config.Model
            });
        }

        TotalFees = await _db.ResellerTransactions
            .Where(t => t.Type == ResellerTransactionType.Fee)
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;

        RecentPayouts = await _db.ResellerTransactions
            .Where(t => t.Type == ResellerTransactionType.Payout)
            .OrderByDescending(t => t.Id).Take(10).ToListAsync();
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAdjustAsync(string resellerId, ResellerTransactionType type, decimal amount, string? reason)
    {
        if (amount <= 0) { TempData["Error"] = "Amount must be positive."; return RedirectToPage(); }
        await _billing.AddTransactionAsync(resellerId, type, amount, reason ?? $"Manual {type} by admin");
        await _audit.LogAsync("Adjust", "ResellerBalance", resellerId, $"{type} {amount}: {reason}");
        TempData["Success"] = $"Applied {type} of {amount:0.00}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostGenerateInvoiceAsync(string resellerId, decimal amount)
    {
        if (amount <= 0) { TempData["Error"] = "Amount must be positive."; return RedirectToPage(); }
        var start = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var inv = await _billing.GenerateMonthlyInvoiceAsync(resellerId, start, start.AddMonths(1).AddDays(-1), amount);
        await _audit.LogAsync("Create", "ResellerInvoice", resellerId, inv.Number);
        TempData["Success"] = $"Invoice {inv.Number} generated.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPayoutAsync(string resellerId, decimal amount, string? reason)
    {
        if (amount <= 0) { TempData["Error"] = "Amount must be positive."; return RedirectToPage(); }
        await _billing.AddTransactionAsync(resellerId, ResellerTransactionType.Payout, amount, reason ?? "Payout processed");
        await _audit.LogAsync("Payout", "Reseller", resellerId, $"{amount}: {reason}");
        TempData["Success"] = $"Payout of {amount:0.00} recorded.";
        return RedirectToPage();
    }
}
