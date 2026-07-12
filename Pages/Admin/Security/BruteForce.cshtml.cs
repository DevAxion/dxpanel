using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Security;

namespace SRXPanel.Pages.Admin.Security;

public class BruteForceModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IBruteForceService _bf;
    private readonly IIpManagerService _ip;

    public BruteForceModel(ApplicationDbContext db, IBruteForceService bf, IIpManagerService ip)
    {
        _db = db;
        _bf = bf;
        _ip = ip;
    }

    public AttackReport Report { get; private set; } = null!;
    public List<BlockedIP> Blocked { get; private set; } = new();
    public List<LoginAttempt> RecentAttempts { get; private set; } = new();
    public List<IpAccessRule> Whitelist { get; private set; } = new();
    public SecuritySettings Settings { get; private set; } = new();

    private async Task LoadAsync()
    {
        Report = await _bf.GetAttackReportAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);
        Blocked = await _bf.GetBlockedIpsAsync();
        RecentAttempts = await _bf.GetRecentAttemptsAsync(30);
        Whitelist = await _ip.GetRulesAsync(IpRuleKind.WhitelistIp);
        Settings = await _db.SecuritySettings.FirstOrDefaultAsync(s => s.Id == 1) ?? new SecuritySettings();
    }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostBlockAsync(string ip, int minutes, string? reason)
    {
        if (!string.IsNullOrWhiteSpace(ip))
        {
            await _bf.BlockIpAsync(ip, minutes > 0 ? TimeSpan.FromMinutes(minutes) : null, reason ?? "Manually blocked by admin", manual: true);
            TempData["Success"] = $"{ip} blocked.";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUnblockAsync(int id)
    {
        await _bf.UnblockIpAsync(id);
        TempData["Success"] = "IP unblocked.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostWhitelistAsync(string ip)
    {
        if (!string.IsNullOrWhiteSpace(ip)) await _ip.AddRuleAsync(IpRuleKind.WhitelistIp, ip, "Brute-force whitelist");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveWhitelistAsync(int id)
    {
        await _ip.RemoveRuleAsync(id);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostThresholdsAsync(int maxAttempts, int blockMinutes, bool protectPanel, bool protectFtp, bool protectSmtp)
    {
        var s = await _db.SecuritySettings.FirstOrDefaultAsync(x => x.Id == 1);
        if (s != null)
        {
            s.BruteForceMaxAttempts = Math.Clamp(maxAttempts, 1, 100);
            s.BruteForceBlockMinutes = Math.Clamp(blockMinutes, 1, 100000);
            s.ProtectPanel = protectPanel;
            s.ProtectFtp = protectFtp;
            s.ProtectSmtp = protectSmtp;
            s.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            TempData["Success"] = "Thresholds saved.";
        }
        return RedirectToPage();
    }
}
