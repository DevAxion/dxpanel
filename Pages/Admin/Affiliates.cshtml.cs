using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Reseller;

namespace SRXPanel.Pages.Admin;

public class AffiliatesModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IAffiliateService _affiliates;
    private readonly IAuditLogService _audit;

    public AffiliatesModel(ApplicationDbContext db, IAffiliateService affiliates, IAuditLogService audit)
    {
        _db = db;
        _affiliates = affiliates;
        _audit = audit;
    }

    public class Row
    {
        public Models.Affiliate Affiliate { get; set; } = null!;
        public string UserName { get; set; } = string.Empty;
        public int Signups { get; set; }
        public List<string> FraudFlags { get; set; } = new();
    }

    public List<Row> Rows { get; set; } = new();
    public List<AffiliatePayoutRequest> PendingPayouts { get; set; } = new();
    public Dictionary<int, string> PayoutAffiliateNames { get; set; } = new();

    private async Task LoadAsync()
    {
        var affiliates = await _db.Affiliates.Include(a => a.User).ToListAsync();
        foreach (var a in affiliates)
        {
            Rows.Add(new Row
            {
                Affiliate = a,
                UserName = a.User?.UserName ?? "—",
                Signups = await _db.AffiliateReferrals.CountAsync(r => r.AffiliateId == a.Id),
                FraudFlags = await _affiliates.DetectFraudAsync(a.Id)
            });
        }

        PendingPayouts = await _db.AffiliatePayoutRequests
            .Where(p => p.Status == AffiliatePayoutStatus.Pending)
            .OrderBy(p => p.Id).ToListAsync();

        foreach (var p in PendingPayouts)
        {
            var aff = affiliates.FirstOrDefault(a => a.Id == p.AffiliateId);
            PayoutAffiliateNames[p.Id] = aff?.User?.UserName ?? "—";
        }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSetRateAsync(int affiliateId, decimal rate)
    {
        var aff = await _db.Affiliates.FindAsync(affiliateId);
        if (aff != null) { aff.CommissionPercent = rate; await _db.SaveChangesAsync(); }
        TempData["Success"] = "Commission rate updated.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAdjustAsync(int affiliateId, decimal amount)
    {
        var aff = await _db.Affiliates.FindAsync(affiliateId);
        if (aff != null)
        {
            aff.PendingBalance += amount;
            aff.TotalEarned += Math.Max(0, amount);
            await _db.SaveChangesAsync();
            await _audit.LogAsync("Adjust", "Affiliate", affiliateId.ToString(), $"balance {amount:+0.00;-0.00}");
        }
        TempData["Success"] = "Balance adjusted.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPayoutDecisionAsync(int payoutId, bool approve)
    {
        var payout = await _db.AffiliatePayoutRequests.FindAsync(payoutId);
        if (payout == null) { TempData["Error"] = "Payout not found."; return RedirectToPage(); }
        var aff = await _db.Affiliates.FindAsync(payout.AffiliateId);

        if (approve)
        {
            payout.Status = AffiliatePayoutStatus.Paid;
            payout.ProcessedAt = DateTime.UtcNow;
            if (aff != null)
            {
                aff.PendingBalance = Math.Max(0, aff.PendingBalance - payout.Amount);
                aff.PaidBalance += payout.Amount;
                var approved = await _db.AffiliateReferrals
                    .Where(r => r.AffiliateId == aff.Id && r.Status != AffiliateReferralStatus.Paid).ToListAsync();
                foreach (var r in approved) r.Status = AffiliateReferralStatus.Paid;
            }
            TempData["Success"] = "Payout approved and marked paid.";
        }
        else
        {
            payout.Status = AffiliatePayoutStatus.Rejected;
            payout.ProcessedAt = DateTime.UtcNow;
            TempData["Success"] = "Payout rejected.";
        }
        await _db.SaveChangesAsync();
        await _audit.LogAsync(approve ? "ApprovePayout" : "RejectPayout", "AffiliatePayout", payoutId.ToString(), null);
        return RedirectToPage();
    }
}
