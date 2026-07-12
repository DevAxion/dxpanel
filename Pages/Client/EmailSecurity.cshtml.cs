using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Security;

namespace SRXPanel.Pages.Client;

public class EmailSecurityModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailSecurityService _svc;

    public EmailSecurityModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IEmailSecurityService svc)
    {
        _db = db;
        _userManager = userManager;
        _svc = svc;
    }

    [BindProperty(SupportsGet = true)] public int? DomainId { get; set; }

    public List<Domain> Domains { get; private set; } = new();
    public Domain? Selected { get; private set; }
    public EmailSecurity? Security { get; private set; }
    public string DkimRecord { get; private set; } = "";
    public int Score { get; private set; }

    private async Task<bool> LoadAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return false;
        Domains = await _db.Domains.Where(d => d.UserId == user.Id).OrderBy(d => d.DomainName).ToListAsync();
        if (Domains.Count == 0) return true;

        Selected = DomainId.HasValue ? Domains.FirstOrDefault(d => d.Id == DomainId) : Domains.First();
        if (Selected == null) return true;

        Security = await _svc.GetOrCreateAsync(Selected.Id);
        DkimRecord = await _svc.GetDkimRecordAsync(Selected.Id);
        Score = await _svc.GetScoreAsync(Selected.Id);
        return true;
    }

    private async Task<int?> OwnedAsync(int domainId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return null;
        return await _db.Domains.AnyAsync(d => d.Id == domainId && d.UserId == user.Id) ? domainId : null;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync()) return Challenge();
        return Page();
    }

    public async Task<IActionResult> OnPostGenerateDkimAsync(int domainId)
    {
        if (await OwnedAsync(domainId) is not int id) return Forbid();
        await _svc.GenerateDkimKeyAsync(id);
        await _svc.EnableDkimAsync(id);
        TempData["Success"] = "DKIM key generated and enabled. Publish the DNS record shown below.";
        return RedirectToPage(new { domainId });
    }

    public async Task<IActionResult> OnPostDisableDkimAsync(int domainId)
    {
        if (await OwnedAsync(domainId) is not int id) return Forbid();
        await _svc.DisableDkimAsync(id);
        return RedirectToPage(new { domainId });
    }

    public async Task<IActionResult> OnPostSetSpfAsync(int domainId, string record)
    {
        if (await OwnedAsync(domainId) is not int id) return Forbid();
        await _svc.SetSpfRecordAsync(id, record);
        TempData["Success"] = "SPF record saved and verified.";
        return RedirectToPage(new { domainId });
    }

    public async Task<IActionResult> OnPostSetDmarcAsync(int domainId, DmarcPolicy policy, string? email, int percentage)
    {
        if (await OwnedAsync(domainId) is not int id) return Forbid();
        await _svc.SetDmarcRecordAsync(id, policy, email, percentage);
        TempData["Success"] = "DMARC policy saved.";
        return RedirectToPage(new { domainId });
    }

    public async Task<IActionResult> OnPostTestAsync(int domainId)
    {
        if (await OwnedAsync(domainId) is not int id) return Forbid();
        var dkim = await _svc.CheckDkimAsync(id);
        var spf = await _svc.CheckSpfAsync(id);
        var dmarc = await _svc.CheckDmarcAsync(id);
        TempData["Success"] = $"DKIM: {(dkim.Valid ? "✓" : "✗")} · SPF: {(spf.Valid ? "✓" : "✗")} · DMARC: {(dmarc.Valid ? "✓" : "✗")}";
        return RedirectToPage(new { domainId });
    }
}
