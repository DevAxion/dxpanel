using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Reseller;

namespace SRXPanel.Pages.Reseller;

public class DashboardModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IResellerService _resellers;

    public DashboardModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IResellerService resellers)
    {
        _db = db;
        _userManager = userManager;
        _resellers = resellers;
    }

    public ResellerProfile? Profile { get; set; }
    public ResellerUsage Usage { get; set; } = new();
    public List<ApplicationUser> RecentClients { get; set; } = new();
    public List<SslCertificate> ExpiringSsl { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        Profile = await _resellers.GetProfileAsync(user.Id);
        if (Profile == null)
        {
            // Reseller role without an allocation profile yet.
            return Page();
        }

        Usage = await _resellers.GetUsageAsync(Profile);

        var clients = await _resellers.GetClientsAsync(user.Id);
        RecentClients = clients.Take(10).ToList();

        var clientIds = clients.Select(c => c.Id).ToList();
        var soon = DateTime.UtcNow.AddDays(30);
        ExpiringSsl = await _db.SslCertificates
            .Include(c => c.Domain)
            .Where(c => clientIds.Contains(c.UserId) && c.ExpiresAt <= soon)
            .OrderBy(c => c.ExpiresAt)
            .Take(10)
            .ToListAsync();

        return Page();
    }
}
