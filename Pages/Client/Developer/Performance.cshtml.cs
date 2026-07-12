using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Developer;

namespace SRXPanel.Pages.Client.Developer;

public class PerformanceModel : PageModel
{
    private readonly IPerformanceService _performance;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public PerformanceModel(IPerformanceService performance, ApplicationDbContext db,
        UserManager<ApplicationUser> userManager)
    {
        _performance = performance;
        _db = db;
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)] public int? DomainId { get; set; }

    public List<Domain> Domains { get; private set; } = new();
    public PerfResult? Result { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        Domains = await _db.Domains.Where(d => d.UserId == user.Id).OrderBy(d => d.DomainName).ToListAsync(ct);

        if (DomainId is int domainId)
        {
            try
            {
                Result = await _performance.TestAsync(user.Id, domainId, ct);
            }
            catch (InvalidOperationException ex)
            {
                Error = ex.Message;
            }
        }

        return Page();
    }
}
