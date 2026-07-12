using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Email;

namespace SRXPanel.Pages.Client.Email;

public class DeliverabilityModel : PageModel
{
    private readonly IDeliverabilityService _deliverability;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;

    public DeliverabilityModel(IDeliverabilityService deliverability, UserManager<ApplicationUser> userManager, ApplicationDbContext db)
    {
        _deliverability = deliverability;
        _userManager = userManager;
        _db = db;
    }

    [BindProperty(SupportsGet = true)] public int? DomainId { get; set; }

    public List<Domain> Domains { get; private set; } = new();
    public Domain? Selected { get; private set; }
    public DeliverabilityScore? Score { get; private set; }
    public List<(DateTime date, int score)> HistoryData { get; private set; } = new();
    public double PlatformAverage { get; private set; }

    private string Uid => _userManager.GetUserId(User)!;

    public async Task OnGetAsync()
    {
        Domains = await _db.Domains.Where(d => d.UserId == Uid).ToListAsync();
        DomainId ??= Domains.FirstOrDefault()?.Id;
        if (DomainId is int id && Domains.Any(d => d.Id == id))
        {
            Selected = Domains.First(d => d.Id == id);
            Score = await _deliverability.GetScoreAsync(id);
            HistoryData = await _deliverability.GetScoreHistoryAsync(id);
            PlatformAverage = await _deliverability.GetPlatformAverageAsync();
        }
    }
}
