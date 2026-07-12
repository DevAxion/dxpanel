using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Email;

namespace SRXPanel.Pages.Client.Email;

public class BouncesModel : PageModel
{
    private readonly IBounceHandlerService _bounces;
    private readonly IMailServerService _mail;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;

    public BouncesModel(IBounceHandlerService bounces, IMailServerService mail,
        UserManager<ApplicationUser> userManager, ApplicationDbContext db)
    {
        _bounces = bounces;
        _mail = mail;
        _userManager = userManager;
        _db = db;
    }

    [BindProperty(SupportsGet = true)] public int? DomainId { get; set; }
    [BindProperty(SupportsGet = true)] public BounceType? Type { get; set; }

    public List<Domain> Domains { get; private set; } = new();
    public Domain? Selected { get; private set; }
    public List<EmailBounce> Bounces { get; private set; } = new();
    public BounceStats Stats { get; private set; } = new(0, 0, 0);
    public MailServerConfig? Config { get; private set; }

    private string Uid => _userManager.GetUserId(User)!;

    private async Task<bool> OwnsAsync(int domainId) => await _db.Domains.AnyAsync(d => d.Id == domainId && d.UserId == Uid);

    private async Task LoadAsync()
    {
        Domains = await _db.Domains.Where(d => d.UserId == Uid).ToListAsync();
        DomainId ??= Domains.FirstOrDefault()?.Id;
        if (DomainId is int id)
        {
            Selected = Domains.FirstOrDefault(d => d.Id == id);
            Bounces = await _bounces.GetBounceListAsync(id, Type);
            Stats = await _bounces.GetStatsAsync(id);
            Config = await _mail.GetConfigAsync(id);
        }
    }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostBlacklistAllAsync(int domainId)
    {
        if (!await OwnsAsync(domainId)) return Forbid();
        var n = await _bounces.BlacklistBouncedAsync(domainId);
        TempData["Success"] = $"{n} hard-bounced address(es) added to the suppression list.";
        return RedirectToPage(new { domainId });
    }

    public async Task<IActionResult> OnPostClearAsync(int domainId)
    {
        if (!await OwnsAsync(domainId)) return Forbid();
        var n = await _bounces.ClearBouncesAsync(domainId);
        TempData["Success"] = $"{n} bounce record(s) cleared.";
        return RedirectToPage(new { domainId });
    }

    public async Task<IActionResult> OnPostAutoToggleAsync(int domainId, bool autoBlacklist)
    {
        if (!await OwnsAsync(domainId)) return Forbid();
        await _mail.UpdateConfigAsync(domainId, c => c.AutoBlacklistBounces = autoBlacklist);
        TempData["Success"] = "Auto-suppression preference saved.";
        return RedirectToPage(new { domainId });
    }

    public async Task<IActionResult> OnGetExportAsync(int domainId)
    {
        if (!await OwnsAsync(domainId)) return Forbid();
        var csv = await _bounces.ExportBouncesAsync(domainId);
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"bounces-{domainId}-{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}
