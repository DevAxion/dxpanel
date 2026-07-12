using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Reseller;

namespace SRXPanel.Pages.Affiliate;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAffiliateService _affiliates;
    private readonly IPlatformSettingsService _platform;

    public IndexModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        IAffiliateService affiliates, IPlatformSettingsService platform)
    {
        _db = db;
        _userManager = userManager;
        _affiliates = affiliates;
        _platform = platform;
    }

    public Models.Affiliate Affiliate { get; set; } = null!;
    public AffiliateStats Stats { get; set; } = new();
    public List<AffiliateReferral> Referrals { get; set; } = new();
    public List<AffiliatePayoutRequest> Payouts { get; set; } = new();
    public string ReferralLink { get; set; } = string.Empty;
    public decimal MinPayout { get; set; }

    private async Task<ApplicationUser?> LoadAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return null;
        var platform = await _platform.GetAsync();
        Affiliate = await _affiliates.GetOrCreateAsync(user.Id, platform.DefaultAffiliateCommission);
        MinPayout = platform.MinPayoutAmount;
        Stats = await _affiliates.GetStatsAsync(Affiliate.Id);
        Referrals = await _db.AffiliateReferrals.Where(r => r.AffiliateId == Affiliate.Id)
            .OrderByDescending(r => r.Id).Take(50).ToListAsync();
        Payouts = await _db.AffiliatePayoutRequests.Where(p => p.AffiliateId == Affiliate.Id)
            .OrderByDescending(p => p.Id).ToListAsync();
        ReferralLink = $"{Request.Scheme}://{Request.Host}/ref/{Affiliate.Code}";
        return user;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (await LoadAsync() == null) return Challenge();
        return Page();
    }

    public async Task<IActionResult> OnPostRequestPayoutAsync(string method, string? details)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var platform = await _platform.GetAsync();
        var affiliate = await _affiliates.GetOrCreateAsync(user.Id, platform.DefaultAffiliateCommission);
        var req = await _affiliates.RequestPayoutAsync(affiliate.Id, platform.MinPayoutAmount, method, details);
        if (req == null)
            TempData["Error"] = $"You need at least {platform.MinPayoutAmount:0.00} pending balance to request a payout.";
        else
            TempData["Success"] = "Payout requested. It will be reviewed by the team.";
        return RedirectToPage();
    }
}
