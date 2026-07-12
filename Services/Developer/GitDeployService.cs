using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Developer;

public record RepoValidation(bool Reachable, string Message, string? DefaultBranch);

public record RepoStatus(GitRepoStatus Status, string? CommitHash, string? CommitMessage,
    DateTime? LastDeployAt, GitDeployStatus? LastDeployStatus);

public interface IGitDeployService
{
    Task<List<GitRepository>> GetReposAsync(string userId);
    Task<GitRepository?> GetRepoAsync(string userId, int id);

    Task<GitRepository> CreateRepoAsync(string userId, int domainId, string repoUrl, string branch,
        string deployPath, int? sshKeyId, string? postDeployCommands = null, bool autoDeploy = true);

    Task UpdateRepoAsync(string userId, int id, string branch, string? postDeployCommands, bool autoDeploy);
    Task DeleteRepoAsync(string userId, int id);

    /// <summary>Queues a deployment and returns its id. Progress streams over SignalR.</summary>
    Task<int> DeployAsync(int repoId, GitTriggerType trigger = GitTriggerType.Manual);

    Task<List<GitDeployment>> GetDeploymentsAsync(int repoId, int limit = 20);
    Task<GitDeployment?> GetDeploymentAsync(int deploymentId);

    string GetWebhookUrl(GitRepository repo, string baseUrl);
    Task<string> RegenerateWebhookSecretAsync(string userId, int id);

    Task<RepoValidation> ValidateRepoAsync(string repoUrl, int? sshKeyId);
    Task<RepoStatus> GetStatusAsync(int repoId);

    /// <summary>Resolves a repository from a webhook call, verifying the shared secret.</summary>
    Task<GitRepository?> FindByWebhookAsync(int repoId, string secret);
}

public class GitDeployService : IGitDeployService
{
    private const string ServiceName = "git";

    private readonly ApplicationDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICommandRunner _runner;

    public GitDeployService(ApplicationDbContext db, IServiceScopeFactory scopeFactory, ICommandRunner runner)
    {
        _db = db;
        _scopeFactory = scopeFactory;
        _runner = runner;
    }

    public Task<List<GitRepository>> GetReposAsync(string userId) =>
        _db.GitRepositories.Include(r => r.Domain).Include(r => r.SshKey)
            .Where(r => r.UserId == userId).OrderByDescending(r => r.CreatedAt).ToListAsync();

    public Task<GitRepository?> GetRepoAsync(string userId, int id) =>
        _db.GitRepositories.Include(r => r.Domain).Include(r => r.SshKey)
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);

    public async Task<GitRepository> CreateRepoAsync(string userId, int domainId, string repoUrl, string branch,
        string deployPath, int? sshKeyId, string? postDeployCommands = null, bool autoDeploy = true)
    {
        if (!IsValidRepoUrl(repoUrl))
            throw new InvalidOperationException("Enter an https:// or git@ repository URL.");

        var domain = await _db.Domains.FirstOrDefaultAsync(d => d.Id == domainId && d.UserId == userId)
            ?? throw new InvalidOperationException("Domain not found.");

        var path = NormalizePath(deployPath);

        if (sshKeyId is int keyId && !await _db.SshKeys.AnyAsync(k => k.Id == keyId && k.UserId == userId))
            throw new InvalidOperationException("SSH key not found.");

        var repo = new GitRepository
        {
            UserId = userId,
            DomainId = domain.Id,
            RepoUrl = repoUrl.Trim(),
            Branch = string.IsNullOrWhiteSpace(branch) ? "main" : branch.Trim(),
            DeployPath = path,
            SshKeyId = sshKeyId,
            WebhookSecret = NewSecret(),
            AutoDeploy = autoDeploy,
            PostDeployCommands = postDeployCommands,
            Status = GitRepoStatus.Idle,
            CreatedAt = DateTime.UtcNow
        };

        _db.GitRepositories.Add(repo);
        await _db.SaveChangesAsync();
        return repo;
    }

    public async Task UpdateRepoAsync(string userId, int id, string branch, string? postDeployCommands, bool autoDeploy)
    {
        var repo = await GetRepoAsync(userId, id) ?? throw new InvalidOperationException("Repository not found.");
        repo.Branch = string.IsNullOrWhiteSpace(branch) ? "main" : branch.Trim();
        repo.PostDeployCommands = postDeployCommands;
        repo.AutoDeploy = autoDeploy;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteRepoAsync(string userId, int id)
    {
        var repo = await GetRepoAsync(userId, id);
        if (repo == null) return;
        _db.GitRepositories.Remove(repo);
        await _db.SaveChangesAsync();
    }

    public Task<List<GitDeployment>> GetDeploymentsAsync(int repoId, int limit = 20) =>
        _db.GitDeployments.Where(d => d.RepositoryId == repoId)
            .OrderByDescending(d => d.StartedAt).Take(limit).ToListAsync();

    public Task<GitDeployment?> GetDeploymentAsync(int deploymentId) =>
        _db.GitDeployments.Include(d => d.Repository).FirstOrDefaultAsync(d => d.Id == deploymentId);

    public string GetWebhookUrl(GitRepository repo, string baseUrl) =>
        $"{baseUrl.TrimEnd('/')}/api/git/webhook/{repo.Id}/{repo.WebhookSecret}";

    public async Task<string> RegenerateWebhookSecretAsync(string userId, int id)
    {
        var repo = await GetRepoAsync(userId, id) ?? throw new InvalidOperationException("Repository not found.");
        repo.WebhookSecret = NewSecret();
        await _db.SaveChangesAsync();
        return repo.WebhookSecret;
    }

    public async Task<GitRepository?> FindByWebhookAsync(int repoId, string secret)
    {
        var repo = await _db.GitRepositories.FirstOrDefaultAsync(r => r.Id == repoId);
        if (repo == null) return null;

        // Constant-time compare so the secret can't be guessed a byte at a time.
        var expected = Encoding.UTF8.GetBytes(repo.WebhookSecret);
        var supplied = Encoding.UTF8.GetBytes(secret ?? "");
        return expected.Length == supplied.Length && CryptographicOperations.FixedTimeEquals(expected, supplied)
            ? repo
            : null;
    }

    public async Task<RepoValidation> ValidateRepoAsync(string repoUrl, int? sshKeyId)
    {
        if (!IsValidRepoUrl(repoUrl))
            return new RepoValidation(false, "Enter an https:// or git@ repository URL.", null);

        var result = await _runner.RunAsync($"git ls-remote --heads {repoUrl}", ServiceName);

        if (_runner.SimulationMode)
            return new RepoValidation(true, "Repository reachable (simulated).", "main");

        if (!result.Success)
            return new RepoValidation(false, "Could not reach the repository. Check the URL and the deploy key.", null);

        // Prefer main, fall back to master, else the first head.
        var heads = Regex.Matches(result.Output, @"refs/heads/(\S+)").Select(m => m.Groups[1].Value).ToList();
        var branch = heads.FirstOrDefault(h => h == "main") ?? heads.FirstOrDefault(h => h == "master") ?? heads.FirstOrDefault();
        return new RepoValidation(true, $"Repository reachable ({heads.Count} branches).", branch);
    }

    public async Task<RepoStatus> GetStatusAsync(int repoId)
    {
        var repo = await _db.GitRepositories.FirstOrDefaultAsync(r => r.Id == repoId)
            ?? throw new InvalidOperationException("Repository not found.");

        var last = await _db.GitDeployments.Where(d => d.RepositoryId == repoId)
            .OrderByDescending(d => d.StartedAt).FirstOrDefaultAsync();

        return new RepoStatus(repo.Status, repo.LastCommitHash, repo.LastCommitMessage, repo.LastDeployAt, last?.Status);
    }

    // ---------------- Deployment ----------------

    public async Task<int> DeployAsync(int repoId, GitTriggerType trigger = GitTriggerType.Manual)
    {
        var repo = await _db.GitRepositories.FirstOrDefaultAsync(r => r.Id == repoId)
            ?? throw new InvalidOperationException("Repository not found.");

        if (repo.Status == GitRepoStatus.Deploying)
            throw new InvalidOperationException("A deployment is already running for this repository.");

        var deployment = new GitDeployment
        {
            RepositoryId = repo.Id,
            TriggerType = trigger,
            Status = GitDeployStatus.Pending,
            StartedAt = DateTime.UtcNow
        };
        _db.GitDeployments.Add(deployment);
        repo.Status = GitRepoStatus.Deploying;
        await _db.SaveChangesAsync();

        var deploymentId = deployment.Id;
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            await RunDeploymentAsync(scope.ServiceProvider, deploymentId);
        });

        return deploymentId;
    }

    private static async Task RunDeploymentAsync(IServiceProvider sp, int deploymentId)
    {
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var runner = sp.GetRequiredService<ICommandRunner>();
        var broadcast = sp.GetRequiredService<IDevToolsBroadcast>();
        var notifications = sp.GetRequiredService<INotificationService>();
        var logger = sp.GetRequiredService<ILogger<GitDeployService>>();

        var deployment = await db.GitDeployments.Include(d => d.Repository).ThenInclude(r => r!.Domain)
            .FirstAsync(d => d.Id == deploymentId);
        var repo = deployment.Repository!;

        var output = new StringBuilder();

        async Task EmitAsync(string line)
        {
            output.AppendLine(line);
            deployment.Output = output.ToString();
            await db.SaveChangesAsync();
            await broadcast.DeployOutputAsync(deploymentId, line);
            await Task.Delay(250);
        }

        try
        {
            deployment.Status = GitDeployStatus.Running;
            await db.SaveChangesAsync();

            var target = $"{repo.Domain?.DocumentRoot?.TrimEnd('/')}{(repo.DeployPath == "/" ? "" : repo.DeployPath)}";
            var keyArg = repo.SshKeyId is int
                ? $"GIT_SSH_COMMAND='ssh -i ~/.ssh/srx_deploy_{repo.SshKeyId} -o StrictHostKeyChecking=accept-new' "
                : "";

            await EmitAsync($"\u001b[36m▸ Deploying {repo.ShortName} ({repo.Branch}) to {target}\u001b[0m");

            await EmitAsync("\u001b[90m$ git fetch --all --prune\u001b[0m");
            var fetch = await runner.RunAsync($"cd {target} && {keyArg}git fetch --all --prune", ServiceName);
            await EmitAsync(fetch.Simulated ? "\u001b[90mFetching origin (simulated)\u001b[0m" : fetch.Output);

            await EmitAsync($"\u001b[90m$ git reset --hard origin/{repo.Branch}\u001b[0m");
            var reset = await runner.RunAsync($"cd {target} && git reset --hard origin/{repo.Branch}", ServiceName);
            await EmitAsync(reset.Simulated ? "\u001b[90mHEAD is now at the tip of origin/" + repo.Branch + " (simulated)\u001b[0m" : reset.Output);

            // Record the commit that was deployed.
            var hashResult = await runner.RunAsync($"cd {target} && git rev-parse HEAD", ServiceName);
            var messageResult = await runner.RunAsync($"cd {target} && git log -1 --pretty=%s", ServiceName);

            var hash = runner.SimulationMode || string.IsNullOrWhiteSpace(hashResult.Output)
                ? SimulatedCommitHash()
                : hashResult.Output.Trim();
            var message = runner.SimulationMode || string.IsNullOrWhiteSpace(messageResult.Output)
                ? SimulatedCommitMessage()
                : messageResult.Output.Trim();

            deployment.CommitHash = hash.Length > 40 ? hash[..40] : hash;
            deployment.CommitMessage = message.Length > 400 ? message[..400] : message;

            await EmitAsync($"\u001b[32m✓ Checked out {deployment.CommitHash[..7]} — {deployment.CommitMessage}\u001b[0m");

            foreach (var command in repo.PostDeployList)
            {
                await EmitAsync($"\u001b[90m$ {command}\u001b[0m");
                var result = await runner.RunAsync($"cd {target} && {command}", ServiceName);

                if (result.Simulated)
                {
                    await EmitAsync($"\u001b[90m{SimulatedPostDeployOutput(command)}\u001b[0m");
                }
                else
                {
                    await EmitAsync(result.Output);
                    if (!result.Success)
                        throw new InvalidOperationException($"Post-deploy command failed (exit {result.ExitCode}): {command}");
                }
            }

            deployment.Status = GitDeployStatus.Success;
            deployment.CompletedAt = DateTime.UtcNow;

            repo.Status = GitRepoStatus.Idle;
            repo.LastDeployAt = deployment.CompletedAt;
            repo.LastCommitHash = deployment.CommitHash;
            repo.LastCommitMessage = deployment.CommitMessage;

            deployment.Output = output.ToString();
            await db.SaveChangesAsync();

            await EmitAsync($"\u001b[32m✓ Deployed successfully in {(deployment.CompletedAt - deployment.StartedAt)?.TotalSeconds:0.0}s\u001b[0m");
            await broadcast.DeployCompletedAsync(deploymentId, true, "Deployment completed successfully.");

            await notifications.NotifyAsync(repo.UserId, "Deployment succeeded",
                $"{repo.ShortName} ({repo.Branch}) deployed to {repo.Domain?.DomainName}.", NotificationType.Success);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Git deployment {DeploymentId} failed", deploymentId);

            output.AppendLine($"\u001b[31m✗ {ex.Message}\u001b[0m");
            deployment.Status = GitDeployStatus.Failed;
            deployment.CompletedAt = DateTime.UtcNow;
            deployment.Output = output.ToString();
            repo.Status = GitRepoStatus.Failed;
            await db.SaveChangesAsync();

            await broadcast.DeployCompletedAsync(deploymentId, false, ex.Message);
            await notifications.NotifyAsync(repo.UserId, "Deployment failed",
                $"{repo.ShortName} ({repo.Branch}): {ex.Message}", NotificationType.Error);
        }
    }

    // ---------------- Helpers ----------------

    private static string NewSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    private static string SimulatedCommitHash() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant();

    private static readonly string[] SampleMessages =
    {
        "Fix null reference in checkout flow",
        "Bump dependencies and rebuild assets",
        "Add caching to the product listing query",
        "Correct typo in the welcome email template",
        "Refactor the deployment script"
    };

    private static string SimulatedCommitMessage() =>
        SampleMessages[RandomNumberGenerator.GetInt32(SampleMessages.Length)];

    /// <summary>Plausible output for the common post-deploy commands while in simulation mode.</summary>
    private static string SimulatedPostDeployOutput(string command)
    {
        if (command.StartsWith("composer", StringComparison.OrdinalIgnoreCase))
            return "Loading composer repositories with package information\nInstalling dependencies from lock file\nGenerating optimized autoload files\n42 packages you are using are looking for funding.";
        if (command.Contains("artisan migrate", StringComparison.OrdinalIgnoreCase))
            return "INFO  Running migrations.\n  2026_01_04_120000_create_sessions_table ... 18ms DONE";
        if (command.StartsWith("npm", StringComparison.OrdinalIgnoreCase))
            return command.Contains("build")
                ? "vite v5.2.0 building for production...\n✓ 384 modules transformed.\n✓ built in 4.21s"
                : "added 612 packages, and audited 613 packages in 9s\nfound 0 vulnerabilities";
        if (command.Contains("cache", StringComparison.OrdinalIgnoreCase))
            return "Cache cleared successfully.";
        return $"{command}: ok";
    }

    private static bool IsValidRepoUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        var u = url.Trim();

        if (u.StartsWith("git@") && u.Contains(':')) return true;
        if (u.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)) return true;
        return Uri.TryCreate(u, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Trim() == "/") return "/";
        var p = path.Trim().Replace('\\', '/');
        if (p.Contains("..")) throw new InvalidOperationException("The deploy path may not contain '..'.");
        return "/" + p.Trim('/');
    }
}
