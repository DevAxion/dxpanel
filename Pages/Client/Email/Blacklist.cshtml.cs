using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Email;

namespace SRXPanel.Pages.Client.Email;

public class BlacklistModel : PageModel
{
    private readonly IBlacklistService _blacklist;
    private readonly IMailServerService _mail;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;

    public BlacklistModel(IBlacklistService blacklist, IMailServerService mail,
        UserManager<ApplicationUser> userManager, ApplicationDbContext db)
    {
        _blacklist = blacklist;
        _mail = mail;
        _userManager = userManager;
        _db = db;
    }

    [BindProperty(SupportsGet = true)] public int? DomainId { get; set; }

    public List<Domain> Domains { get; private set; } = new();
    public Domain? Selected { get; private set; }
    public BlacklistAll? Result { get; private set; }
    public List<BlacklistCheck> History { get; private set; } = new();
    public MailServerConfig? Config { get; private set; }

    private string Uid => _userManager.GetUserId(User)!;

    private async Task LoadAsync()
    {
        Domains = await _db.Domains.Where(d => d.UserId == Uid).ToListAsync();
        DomainId ??= Domains.FirstOrDefault()?.Id;
        if (DomainId is int id)
        {
            Selected = Domains.FirstOrDefault(d => d.Id == id);
            History = await _blacklist.GetCheckHistoryAsync(id);
            Config = await _mail.GetConfigAsync(id);
        }
    }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostCheckAsync(int domainId)
    {
        var owns = await _db.Domains.AnyAsync(d => d.Id == domainId && d.UserId == Uid);
        if (!owns) return Forbid();
        Result = await _blacklist.CheckAllAsync(domainId, Uid);
        DomainId = domainId;
        await LoadAsync();
        TempData["Success"] = Result.AnyListed ? "Check complete — listings found." : "Check complete — all clean.";
        return Page();
    }

    public async Task<IActionResult> OnPostAutoCheckAsync(int domainId, bool enabled, string schedule)
    {
        var owns = await _db.Domains.AnyAsync(d => d.Id == domainId && d.UserId == Uid);
        if (!owns) return Forbid();
        await _blacklist.ScheduleAutoCheckAsync(domainId, enabled, schedule);
        TempData["Success"] = "Auto-check schedule updated.";
        return RedirectToPage(new { domainId });
    }

    public async Task<IActionResult> OnPostAlertAsync(int domainId, bool alertOnBlacklist)
    {
        var owns = await _db.Domains.AnyAsync(d => d.Id == domainId && d.UserId == Uid);
        if (!owns) return Forbid();
        await _mail.UpdateConfigAsync(domainId, c => c.AlertOnBlacklist = alertOnBlacklist);
        TempData["Success"] = "Alert preference saved.";
        return RedirectToPage(new { domainId });
    }
}
