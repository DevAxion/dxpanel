using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services.Cloudflare;

namespace SRXPanel.Pages.Client.Cloudflare;

public class IndexModel : PageModel
{
    private readonly ICloudflareManager _cf;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(ICloudflareManager cf, UserManager<ApplicationUser> userManager)
    {
        _cf = cf;
        _userManager = userManager;
    }

    public CloudflareAccount? Account { get; private set; }
    public List<CloudflareDomain> Domains { get; private set; } = new();
    public List<Domain> Linkable { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        Account = await _cf.GetAccountAsync(user.Id);
        if (Account != null)
        {
            Domains = await _cf.GetLinkedDomainsAsync(user.Id);
            Linkable = await _cf.GetLinkableDomainsAsync(user.Id);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostUnlinkAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        await _cf.UnlinkDomainAsync(user.Id, id);
        TempData["Success"] = "Domain unlinked from Cloudflare. The zone itself was left untouched.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDisconnectAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        await _cf.DisconnectAsync(user.Id);
        TempData["Success"] = "Cloudflare account disconnected.";
        return RedirectToPage();
    }
}
