using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Email;

namespace SRXPanel.Pages.Client.Email;

public class MailServerModel : PageModel
{
    private readonly IMailServerService _mail;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;

    public MailServerModel(IMailServerService mail, UserManager<ApplicationUser> userManager, ApplicationDbContext db)
    {
        _mail = mail;
        _userManager = userManager;
        _db = db;
    }

    [BindProperty(SupportsGet = true)] public int? DomainId { get; set; }

    public List<Domain> Domains { get; private set; } = new();
    public Domain? Selected { get; private set; }
    public MailServerConfig? Config { get; private set; }
    public PostfixStatus Postfix { get; private set; } = new(false, 0, 0, 0);
    public DovecotStatus Dovecot { get; private set; } = new(false, 0, 0);

    private string Uid => _userManager.GetUserId(User)!;
    private async Task<bool> OwnsAsync(int domainId) => await _db.Domains.AnyAsync(d => d.Id == domainId && d.UserId == Uid);

    private async Task LoadAsync()
    {
        Domains = await _db.Domains.Where(d => d.UserId == Uid).ToListAsync();
        DomainId ??= Domains.FirstOrDefault()?.Id;
        if (DomainId is int id && await OwnsAsync(id))
        {
            Selected = Domains.FirstOrDefault(d => d.Id == id);
            Config = await _mail.GetConfigAsync(id);
            Postfix = await _mail.GetPostfixStatusAsync();
            Dovecot = await _mail.GetDovecotStatusAsync();
        }
    }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostReloadAsync(int domainId, string service)
    {
        if (!await OwnsAsync(domainId)) return Forbid();
        var result = service == "dovecot" ? await _mail.ReloadDovecotAsync() : await _mail.ReloadPostfixAsync();
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToPage(new { domainId });
    }

    public async Task<IActionResult> OnPostSpamAsync(int domainId, double spamThreshold, int spamRetentionDays, bool quarantineEnabled)
    {
        if (!await OwnsAsync(domainId)) return Forbid();
        await _mail.UpdateConfigAsync(domainId, c =>
        {
            c.SpamThreshold = Math.Clamp(spamThreshold, 1, 10);
            c.SpamRetentionDays = Math.Clamp(spamRetentionDays, 0, 365);
            c.QuarantineEnabled = quarantineEnabled;
        });
        TempData["Success"] = "Spam filter settings saved.";
        return RedirectToPage(new { domainId });
    }
}
