using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services.Developer;

namespace SRXPanel.Pages.Client.Developer;

public class GitRepoModel : PageModel
{
    private readonly IGitDeployService _git;
    private readonly UserManager<ApplicationUser> _userManager;

    public GitRepoModel(IGitDeployService git, UserManager<ApplicationUser> userManager)
    {
        _git = git;
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)] public int Id { get; set; }
    [BindProperty(SupportsGet = true)] public int? DeploymentId { get; set; }

    public GitRepository Repo { get; private set; } = null!;
    public List<GitDeployment> Deployments { get; private set; } = new();
    public string WebhookUrl { get; private set; } = "";

    /// <summary>The deployment to stream live, when one was just started.</summary>
    public GitDeployment? Live { get; private set; }

    private string BaseUrl => $"{Request.Scheme}://{Request.Host}";

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var repo = await _git.GetRepoAsync(user.Id, Id);
        if (repo == null) return NotFound();

        Repo = repo;
        WebhookUrl = _git.GetWebhookUrl(repo, BaseUrl);
        Deployments = await _git.GetDeploymentsAsync(repo.Id, 20);

        if (DeploymentId is int deploymentId)
            Live = Deployments.FirstOrDefault(d => d.Id == deploymentId);

        return Page();
    }

    /// <summary>Polling fallback for the live deployment log when SignalR is unavailable.</summary>
    public async Task<IActionResult> OnGetStatusAsync(int deploymentId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var deployment = await _git.GetDeploymentAsync(deploymentId);
        if (deployment?.Repository?.UserId != user.Id) return NotFound();

        return new JsonResult(new
        {
            status = deployment.Status.ToString(),
            output = deployment.Output,
            commit = deployment.CommitHash,
            message = deployment.CommitMessage
        });
    }

    public async Task<IActionResult> OnPostUpdateAsync(string branch, string? postDeployCommands, bool autoDeploy)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        try
        {
            await _git.UpdateRepoAsync(user.Id, Id, branch, postDeployCommands, autoDeploy);
            TempData["Success"] = "Repository settings saved.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostRegenerateSecretAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        await _git.RegenerateWebhookSecretAsync(user.Id, Id);
        TempData["Success"] = "Webhook secret regenerated. Update the webhook URL in your git provider.";
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostDeployAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var repo = await _git.GetRepoAsync(user.Id, Id);
        if (repo == null) return NotFound();

        try
        {
            var deploymentId = await _git.DeployAsync(repo.Id);
            return RedirectToPage(new { id = Id, deploymentId });
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToPage(new { id = Id });
        }
    }
}
