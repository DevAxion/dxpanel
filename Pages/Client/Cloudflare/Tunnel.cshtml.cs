using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Cloudflare;

namespace SRXPanel.Pages.Client.Cloudflare;

public class TunnelModel : PageModel
{
    private readonly ICloudflareManager _cf;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public TunnelModel(ICloudflareManager cf, ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _cf = cf;
        _db = db;
        _userManager = userManager;
    }

    public CloudflareAccount? Account { get; private set; }
    public List<CloudflareTunnel> Tunnels { get; private set; } = new();
    public string? NewSecret { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        Account = await _cf.GetAccountAsync(user.Id);
        if (Account != null)
            Tunnels = await _db.CloudflareTunnels.Where(t => t.UserId == user.Id)
                .OrderByDescending(t => t.CreatedAt).ToListAsync();

        NewSecret = TempData["NewTunnelSecret"] as string;
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(string name, string? hostnames)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var account = await _cf.GetAccountAsync(user.Id);
        if (account == null)
        {
            TempData["Error"] = "Connect a Cloudflare account first.";
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Give the tunnel a name.";
            return RedirectToPage();
        }

        var created = await _cf.Gateway.CreateTunnelAsync(account, name.Trim());
        if (created == null)
        {
            TempData["Error"] = "Cloudflare could not create the tunnel.";
            return RedirectToPage();
        }

        _db.CloudflareTunnels.Add(new CloudflareTunnel
        {
            CloudflareAccountId = account.Id,
            UserId = user.Id,
            Name = name.Trim(),
            TunnelId = created.Value.tunnelId,
            Hostnames = hostnames,
            Status = CfTunnelStatus.Inactive,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        TempData["NewTunnelSecret"] = created.Value.secret;
        TempData["Success"] = "Tunnel created. Copy the run token now — it is shown only once.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var tunnel = await _db.CloudflareTunnels.Include(t => t.Account)
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == user.Id);
        if (tunnel != null)
        {
            await _cf.Gateway.DeleteTunnelAsync(tunnel.Account!, tunnel.TunnelId);
            _db.CloudflareTunnels.Remove(tunnel);
            await _db.SaveChangesAsync();
        }

        TempData["Success"] = "Tunnel deleted.";
        return RedirectToPage();
    }
}
