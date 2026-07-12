using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services.Cloudflare;

namespace SRXPanel.Pages.Client.Cloudflare;

public class MigrateModel : PageModel
{
    private readonly ICloudflareManager _cf;
    private readonly UserManager<ApplicationUser> _userManager;

    public MigrateModel(ICloudflareManager cf, UserManager<ApplicationUser> userManager)
    {
        _cf = cf;
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)] public int DomainId { get; set; }

    public CloudflareDomain Cf { get; private set; } = null!;
    public List<DnsDiffRow> Diff { get; private set; } = new();

    public int PanelOnly => Diff.Count(d => d.State == "panel-only");
    public int CloudflareOnly => Diff.Count(d => d.State == "cloudflare-only");
    public int Differs => Diff.Count(d => d.State == "differs");

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var cf = await _cf.GetLinkedDomainAsync(user.Id, DomainId);
        if (cf == null) return NotFound();

        Cf = cf;
        Diff = await _cf.CompareDnsAsync(user.Id, cf);
        return Page();
    }

    public async Task<IActionResult> OnPostSyncAsync(DnsSyncDirection direction)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var cf = await _cf.GetLinkedDomainAsync(user.Id, DomainId);
        if (cf == null) return NotFound();

        var changes = await _cf.SyncDnsAsync(user.Id, cf, direction);
        TempData["Success"] = changes == 0
            ? "DNS is already in sync — nothing to copy."
            : $"Synced {changes} record(s) ({direction}).";

        return RedirectToPage("/Client/Cloudflare/Domain", new { domainId = DomainId, tab = "dns" });
    }
}
