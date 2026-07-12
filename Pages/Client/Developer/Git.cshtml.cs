using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Developer;

namespace SRXPanel.Pages.Client.Developer;

public class GitModel : PageModel
{
    private readonly IGitDeployService _git;
    private readonly ISshKeyService _ssh;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _auditLog;

    public GitModel(IGitDeployService git, ISshKeyService ssh, ApplicationDbContext db,
        UserManager<ApplicationUser> userManager, IAuditLogService auditLog)
    {
        _git = git;
        _ssh = ssh;
        _db = db;
        _userManager = userManager;
        _auditLog = auditLog;
    }

    public List<GitRepository> Repositories { get; private set; } = new();
    public List<Domain> Domains { get; private set; } = new();
    public List<SshKey> SshKeys { get; private set; } = new();

    /// <summary>Last deployment per repository, for the status column.</summary>
    public Dictionary<int, GitDeployment?> LastDeployments { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        await LoadAsync(user.Id);
        return Page();
    }

    private async Task LoadAsync(string userId)
    {
        Repositories = await _git.GetReposAsync(userId);
        Domains = await _db.Domains.Where(d => d.UserId == userId).OrderBy(d => d.DomainName).ToListAsync();
        SshKeys = await _ssh.GetKeysAsync(userId);

        foreach (var repo in Repositories)
            LastDeployments[repo.Id] = (await _git.GetDeploymentsAsync(repo.Id, 1)).FirstOrDefault();
    }

    /// <summary>Connection test from the "Add repository" form.</summary>
    public async Task<IActionResult> OnGetValidateAsync(string repoUrl, int? sshKeyId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var validation = await _git.ValidateRepoAsync(repoUrl ?? "", sshKeyId);
        return new JsonResult(new { reachable = validation.Reachable, message = validation.Message, branch = validation.DefaultBranch });
    }

    public async Task<IActionResult> OnPostCreateAsync(int domainId, string repoUrl, string branch,
        string deployPath, int? sshKeyId, string? postDeployCommands, bool autoDeploy)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        try
        {
            var repo = await _git.CreateRepoAsync(user.Id, domainId, repoUrl, branch, deployPath,
                sshKeyId == 0 ? null : sshKeyId, postDeployCommands, autoDeploy);

            await _auditLog.LogAsync("Create", "GitRepository", repo.Id.ToString(), repo.RepoUrl);
            TempData["Success"] = $"Repository added. Add the webhook URL to {repo.ShortName} to deploy on push.";
            return RedirectToPage("/Client/Developer/GitRepo", new { id = repo.Id });
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToPage();
        }
    }

    public async Task<IActionResult> OnPostDeployAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var repo = await _git.GetRepoAsync(user.Id, id);
        if (repo == null) return NotFound();

        try
        {
            var deploymentId = await _git.DeployAsync(repo.Id);
            await _auditLog.LogAsync("Deploy", "GitRepository", repo.Id.ToString(), repo.Branch);
            return RedirectToPage("/Client/Developer/GitRepo", new { id = repo.Id, deploymentId });
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToPage();
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        await _git.DeleteRepoAsync(user.Id, id);
        await _auditLog.LogAsync("Delete", "GitRepository", id.ToString(), "");
        TempData["Success"] = "Repository removed. Deployed files were left in place.";
        return RedirectToPage();
    }
}
