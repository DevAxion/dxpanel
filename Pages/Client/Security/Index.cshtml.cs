using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services.Security;

namespace SRXPanel.Pages.Client.Security;

public class IndexModel : PageModel
{
    private readonly ISecurityScoreService _score;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(ISecurityScoreService score, UserManager<ApplicationUser> userManager)
    {
        _score = score;
        _userManager = userManager;
    }

    public SecurityScore Score { get; private set; } = new(0, new());
    public List<SecurityEvent> Timeline { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return;
        Score = await _score.GetScoreAsync(user);
        Timeline = await _score.GetTimelineAsync(user.Id);
    }
}
