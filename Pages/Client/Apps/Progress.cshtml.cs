using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services.Apps;

namespace SRXPanel.Pages.Client.Apps;

public class ProgressModel : PageModel
{
    private readonly IAppInstallerService _installer;
    private readonly UserManager<ApplicationUser> _userManager;

    public ProgressModel(IAppInstallerService installer, UserManager<ApplicationUser> userManager)
    {
        _installer = installer;
        _userManager = userManager;
    }

    public AppInstallJob Job { get; private set; } = null!;
    public AppInstallation? Installation { get; private set; }

    public async Task<IActionResult> OnGetAsync(int jobId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var job = await _installer.GetJobAsync(jobId);
        if (job == null || job.UserId != user.Id) return NotFound();
        Job = job;

        if (job.InstallationId is int id)
            Installation = await _installer.GetInstallationDetailsAsync(user.Id, id);

        return Page();
    }

    /// <summary>Polling fallback used when SignalR is unavailable.</summary>
    public async Task<IActionResult> OnGetStatusAsync(int jobId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var job = await _installer.GetJobAsync(jobId);
        if (job == null || job.UserId != user.Id) return NotFound();

        return new JsonResult(new
        {
            progress = job.Progress,
            step = job.CurrentStep,
            status = job.Status.ToString(),
            log = job.Log,
            installationId = job.InstallationId
        });
    }
}
