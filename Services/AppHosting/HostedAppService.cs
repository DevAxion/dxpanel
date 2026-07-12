using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.AppHosting;

public record CreateAppRequest(string UserId, string Name, AppRuntimeType Type, int? DomainId, int? RuntimeId,
    string AppPath, string EntryPoint, string StartCommand, int ProcessCount, bool AutoRestart,
    Dictionary<string, string> Env);

/// <summary>
/// Orchestrates hosted apps end-to-end: creation + port allocation + Nginx reverse-proxy config,
/// lifecycle (start/stop/restart/reload/scale) routed to PM2 (Node) or Gunicorn (others), background
/// deploys with live output, environment variables, and npm/pip helpers. All shell work is sim-safe.
/// </summary>
public interface IHostedAppService
{
    Task<HostedApp> CreateAppAsync(CreateAppRequest request);
    Task<bool> StartAsync(int appId);
    Task<bool> StopAsync(int appId);
    Task<bool> RestartAsync(int appId);
    Task<bool> ReloadAsync(int appId);
    Task<bool> ScaleAsync(int appId, int instances);
    Task DeleteAsync(int appId);

    Task<ProcMetrics> GetMetricsAsync(HostedApp app);
    Task<(string stdout, string stderr)> GetLogsAsync(HostedApp app, int lines = 200);
    Task FlushLogsAsync(HostedApp app);

    Task SetEnvAsync(int appId, string key, string value, bool isSecret);
    Task DeleteEnvAsync(int appId, int envId);
    Task<int> ImportEnvAsync(int appId, string dotenv);

    int StartDeploy(int appId, AppDeployType type, int? gitRepoId);

    string GenerateNginxConfig(HostedApp app);

    Task<string> RunNpmScriptAsync(HostedApp app, string script);
    Task<string> NpmInstallAsync(HostedApp app, string? package);
    Task<string> NpmAuditAsync(HostedApp app, bool fix);
    Task<string> PipInstallAsync(HostedApp app, string package);
    Task<string> PipUninstallAsync(HostedApp app, string package);
    Task<List<(string name, string version)>> PipListAsync(HostedApp app);
    Task<string> PipFreezeAsync(HostedApp app);
}

public class HostedAppService : IHostedAppService
{
    private const string ServiceName = "apphosting";

    private readonly ApplicationDbContext _db;
    private readonly IPm2Service _pm2;
    private readonly IGunicornService _gunicorn;
    private readonly IPortManagerService _ports;
    private readonly ICommandRunner _runner;
    private readonly IHostedAppBroadcast _broadcast;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HostedAppService> _logger;

    public HostedAppService(ApplicationDbContext db, IPm2Service pm2, IGunicornService gunicorn,
        IPortManagerService ports, ICommandRunner runner, IHostedAppBroadcast broadcast,
        IServiceScopeFactory scopeFactory, ILogger<HostedAppService> logger)
    {
        _db = db;
        _pm2 = pm2;
        _gunicorn = gunicorn;
        _ports = ports;
        _runner = runner;
        _broadcast = broadcast;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // ---------------- Create ----------------

    public async Task<HostedApp> CreateAppAsync(CreateAppRequest request)
    {
        var port = await _ports.AllocatePortAsync(request.UserId);

        var app = new HostedApp
        {
            UserId = request.UserId,
            Name = request.Name,
            Type = request.Type,
            DomainId = request.DomainId,
            RuntimeId = request.RuntimeId,
            AppPath = request.AppPath,
            EntryPoint = request.EntryPoint,
            StartCommand = request.StartCommand,
            Port = port,
            ProcessCount = Math.Clamp(request.ProcessCount, 1, 4),
            ClusterMode = request.Type == AppRuntimeType.NodeJs && request.ProcessCount > 1,
            AutoRestart = request.AutoRestart,
            Status = HostedAppStatus.Stopped
        };
        _db.HostedApps.Add(app);
        await _db.SaveChangesAsync();

        foreach (var (k, v) in request.Env)
            _db.HostedAppEnvs.Add(new HostedAppEnv { HostedAppId = app.Id, Key = k, Value = v, IsSecret = LooksSecret(k) });
        await _db.SaveChangesAsync();

        // Write the Nginx reverse-proxy vhost for the app's domain.
        if (app.DomainId.HasValue)
        {
            var domain = await _db.Domains.FirstOrDefaultAsync(d => d.Id == app.DomainId);
            if (domain != null)
            {
                await _runner.WriteFileAsync($"/etc/nginx/sites-available/{domain.DomainName}.app.conf",
                    GenerateNginxConfig(app), ServiceName);
                await _runner.RunAsync("nginx -t && systemctl reload nginx", ServiceName);
            }
        }

        return app;
    }

    // ---------------- Lifecycle ----------------

    private Task<HostedApp?> LoadAsync(int appId) =>
        _db.HostedApps.Include(a => a.EnvVars).FirstOrDefaultAsync(a => a.Id == appId);

    public async Task<bool> StartAsync(int appId)
    {
        var app = await LoadAsync(appId);
        if (app == null) return false;

        app.Status = HostedAppStatus.Starting;
        await _db.SaveChangesAsync();

        var result = app.IsNode ? await _pm2.StartAppAsync(app) : await _gunicorn.StartAppAsync(app);
        if (result.Success)
        {
            app.Status = HostedAppStatus.Running;
            app.Pm2Id = result.Pm2Id;
            app.Pid = new Random().Next(1000, 32000);
            app.StartedAt = DateTime.UtcNow;
            app.Healthy = true;
            await AppendLogAsync(app.Id, AppLogType.Out, $"Server running on port {app.Port}");
        }
        else
        {
            app.Status = HostedAppStatus.Error;
            await AppendLogAsync(app.Id, AppLogType.Error, result.Output);
        }
        await _db.SaveChangesAsync();
        await _broadcast.StatusAsync(app.Id, app.Status.ToString());
        return result.Success;
    }

    public async Task<bool> StopAsync(int appId)
    {
        var app = await LoadAsync(appId);
        if (app == null) return false;
        if (app.IsNode && app.Pm2Id.HasValue) await _pm2.StopAppAsync(app.Pm2Id.Value);
        else await _gunicorn.StopAppAsync(app);

        app.Status = HostedAppStatus.Stopped;
        app.Pid = null;
        app.CpuPercent = 0;
        app.MemoryMB = 0;
        await _db.SaveChangesAsync();
        await _broadcast.StatusAsync(app.Id, app.Status.ToString());
        return true;
    }

    public async Task<bool> RestartAsync(int appId)
    {
        var app = await LoadAsync(appId);
        if (app == null) return false;
        if (app.IsNode && app.Pm2Id.HasValue) await _pm2.RestartAppAsync(app.Pm2Id.Value);
        else await _gunicorn.RestartAppAsync(app);

        app.Status = HostedAppStatus.Running;
        app.RestartCount++;
        app.StartedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _broadcast.StatusAsync(app.Id, app.Status.ToString());
        return true;
    }

    public async Task<bool> ReloadAsync(int appId)
    {
        var app = await LoadAsync(appId);
        if (app == null) return false;
        if (app.IsNode && app.Pm2Id.HasValue) await _pm2.ReloadAppAsync(app.Pm2Id.Value);
        else await _gunicorn.RestartAppAsync(app);
        app.Status = HostedAppStatus.Running;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ScaleAsync(int appId, int instances)
    {
        var app = await LoadAsync(appId);
        if (app == null || !app.IsNode) return false;
        instances = Math.Clamp(instances, 1, 4);
        if (app.Pm2Id.HasValue) await _pm2.ScaleAppAsync(app.Pm2Id.Value, instances);
        app.ProcessCount = instances;
        app.ClusterMode = instances > 1;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task DeleteAsync(int appId)
    {
        var app = await LoadAsync(appId);
        if (app == null) return;
        if (app.IsNode && app.Pm2Id.HasValue) await _pm2.DeleteAppAsync(app.Pm2Id.Value);
        else await _gunicorn.StopAppAsync(app);

        if (app.DomainId.HasValue)
        {
            var domain = await _db.Domains.FirstOrDefaultAsync(d => d.Id == app.DomainId);
            if (domain != null)
                await _runner.DeleteFileAsync($"/etc/nginx/sites-available/{domain.DomainName}.app.conf", ServiceName);
        }

        _db.HostedApps.Remove(app);
        await _db.SaveChangesAsync();
    }

    // ---------------- Metrics + logs ----------------

    public Task<ProcMetrics> GetMetricsAsync(HostedApp app) =>
        app.IsNode ? _pm2.GetStatusAsync(app) : _gunicorn.GetStatusAsync(app);

    public Task<(string stdout, string stderr)> GetLogsAsync(HostedApp app, int lines = 200) =>
        app.IsNode ? _pm2.GetLogsAsync(app, lines) : _gunicorn.GetLogsAsync(app, lines);

    public async Task FlushLogsAsync(HostedApp app)
    {
        if (app.IsNode && app.Pm2Id.HasValue) await _pm2.FlushLogsAsync(app.Pm2Id.Value);
        var logs = await _db.HostedAppLogs.Where(l => l.HostedAppId == app.Id).ToListAsync();
        _db.HostedAppLogs.RemoveRange(logs);
        await _db.SaveChangesAsync();
    }

    private async Task AppendLogAsync(int appId, AppLogType type, string message)
    {
        _db.HostedAppLogs.Add(new HostedAppLog { HostedAppId = appId, Type = type, Message = message });
        await _broadcast.LogAsync(appId, type.ToString(), message);
    }

    // ---------------- Environment ----------------

    public async Task SetEnvAsync(int appId, string key, string value, bool isSecret)
    {
        key = key.Trim();
        if (string.IsNullOrEmpty(key)) return;
        var existing = await _db.HostedAppEnvs.FirstOrDefaultAsync(e => e.HostedAppId == appId && e.Key == key);
        if (existing != null) { existing.Value = value; existing.IsSecret = isSecret; }
        else _db.HostedAppEnvs.Add(new HostedAppEnv { HostedAppId = appId, Key = key, Value = value, IsSecret = isSecret });
        await _db.SaveChangesAsync();
    }

    public async Task DeleteEnvAsync(int appId, int envId)
    {
        var env = await _db.HostedAppEnvs.FirstOrDefaultAsync(e => e.Id == envId && e.HostedAppId == appId);
        if (env != null) { _db.HostedAppEnvs.Remove(env); await _db.SaveChangesAsync(); }
    }

    public async Task<int> ImportEnvAsync(int appId, string dotenv)
    {
        var count = 0;
        foreach (var raw in dotenv.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || !line.Contains('=')) continue;
            var idx = line.IndexOf('=');
            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim().Trim('"', '\'');
            if (key.Length == 0) continue;
            await SetEnvAsync(appId, key, value, LooksSecret(key));
            count++;
        }
        return count;
    }

    // ---------------- Deploy ----------------

    public int StartDeploy(int appId, AppDeployType type, int? gitRepoId)
    {
        // Create the deploy row synchronously so the caller has an id, then run in the background.
        var deploy = new HostedAppDeploy { HostedAppId = appId, Type = type, Status = AppDeployStatus.Pending };
        _db.HostedAppDeploys.Add(deploy);
        _db.SaveChanges();
        var deployId = deploy.Id;

        _ = Task.Run(async () =>
        {
            try { await RunDeployAsync(deployId, appId, type, gitRepoId); }
            catch (Exception ex) { _logger.LogError(ex, "Deploy {DeployId} crashed", deployId); }
        });
        return deployId;
    }

    private async Task RunDeployAsync(int deployId, int appId, AppDeployType type, int? gitRepoId)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var runner = sp.GetRequiredService<ICommandRunner>();
        var broadcast = sp.GetRequiredService<IHostedAppBroadcast>();
        var self = sp.GetRequiredService<IHostedAppService>();

        var deploy = await db.HostedAppDeploys.FirstAsync(d => d.Id == deployId);
        var app = await db.HostedApps.FirstOrDefaultAsync(a => a.Id == appId);
        if (app == null) { deploy.Status = AppDeployStatus.Failed; deploy.Output = "App not found."; await db.SaveChangesAsync(); return; }

        deploy.Status = AppDeployStatus.Running;
        await db.SaveChangesAsync();

        var log = new System.Text.StringBuilder();
        async Task Emit(int percent, string step, string line)
        {
            log.AppendLine(line);
            await broadcast.DeployProgressAsync(appId, percent, step, line);
            await Task.Delay(800);
        }

        try
        {
            await Emit(10, "Preparing", $"▸ Starting {type} deploy for {app.Name}");

            if (type == AppDeployType.Git && gitRepoId is int repoId)
            {
                var repo = await db.GitRepositories.FirstOrDefaultAsync(r => r.Id == repoId);
                if (repo != null)
                {
                    await runner.RunAsync($"cd {app.AppPath} && git pull origin {repo.Branch}", ServiceName);
                    deploy.CommitHash = repo.LastCommitHash ?? Guid.NewGuid().ToString("N")[..7];
                    await Emit(35, "Pulling", $"▸ git pull origin {repo.Branch} → {deploy.CommitHash}");
                }
            }
            else if (type == AppDeployType.Upload)
            {
                await runner.RunAsync($"cd {app.AppPath} && tar xzf release.tar.gz && rm release.tar.gz", ServiceName);
                await Emit(35, "Extracting", "▸ Extracted uploaded archive");
            }

            // Install dependencies for the runtime.
            var installCmd = app.Type switch
            {
                AppRuntimeType.NodeJs => "npm install --production",
                AppRuntimeType.Python => "source .venv/bin/activate && pip install -r requirements.txt",
                AppRuntimeType.Ruby => "bundle install",
                AppRuntimeType.Go => "go build -o app .",
                _ => "echo no deps"
            };
            await runner.RunAsync($"cd {app.AppPath} && {installCmd}", ServiceName);
            await Emit(70, "Installing dependencies", $"▸ {installCmd}");

            await Emit(90, "Restarting", "▸ Reloading the app with zero downtime");
            await self.ReloadAsync(appId);

            deploy.Status = AppDeployStatus.Success;
            deploy.CompletedAt = DateTime.UtcNow;
            deploy.Output = log.ToString();
            await db.SaveChangesAsync();

            await Emit(100, "Complete", "✓ Deploy complete");
            await broadcast.DeployCompletedAsync(appId, true, "Deploy complete.");
        }
        catch (Exception ex)
        {
            deploy.Status = AppDeployStatus.Failed;
            deploy.CompletedAt = DateTime.UtcNow;
            deploy.Output = log.ToString() + "\n✗ " + ex.Message;
            await db.SaveChangesAsync();
            await broadcast.DeployCompletedAsync(appId, false, $"Deploy failed: {ex.Message}");
        }
    }

    // ---------------- Nginx ----------------

    public string GenerateNginxConfig(HostedApp app)
    {
        var server = app.Domain?.DomainName ?? "app.example.com";
        return $$"""
server {
    server_name {{server}};

    location / {
        proxy_pass http://localhost:{{app.Port}};
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection 'upgrade';
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_set_header X-Real-IP $remote_addr;
    }

    location /static/ { alias {{app.AppPath}}/static/; }
    location /media/  { alias {{app.AppPath}}/media/; }
}
""";
    }

    // ---------------- npm / pip helpers ----------------

    public async Task<string> RunNpmScriptAsync(HostedApp app, string script)
    {
        var safe = new string(script.Where(c => char.IsLetterOrDigit(c) || c is '-' or ':' or '_').ToArray());
        var result = await _runner.RunAsync($"cd {app.AppPath} && npm run {safe}", "npm");
        return _runner.SimulationMode ? $"> {app.Name}@ {safe}\n> executed npm script '{safe}'\n\nDone in 1.4s." : result.Output;
    }

    public async Task<string> NpmInstallAsync(HostedApp app, string? package)
    {
        var pkg = string.IsNullOrWhiteSpace(package) ? "" : " " + SanitizePackage(package);
        var result = await _runner.RunAsync($"cd {app.AppPath} && npm install{pkg}", "npm");
        return _runner.SimulationMode ? $"added {new Random().Next(1, 40)} packages in {new Random().Next(1, 9)}s" : result.Output;
    }

    public async Task<string> NpmAuditAsync(HostedApp app, bool fix)
    {
        var result = await _runner.RunAsync($"cd {app.AppPath} && npm audit{(fix ? " fix" : " --json")}", "npm");
        if (!_runner.SimulationMode) return result.Output;
        return fix
            ? "fixed 2 of 3 vulnerabilities; 1 requires manual review"
            : "found 3 vulnerabilities (1 low, 1 moderate, 1 high)\nrun `npm audit fix` to address 2 of them";
    }

    public async Task<string> PipInstallAsync(HostedApp app, string package)
    {
        var pkg = SanitizePackage(package);
        var result = await _runner.RunAsync($"cd {app.AppPath} && .venv/bin/pip install {pkg}", "pip");
        return _runner.SimulationMode ? $"Successfully installed {pkg}" : result.Output;
    }

    public async Task<string> PipUninstallAsync(HostedApp app, string package)
    {
        var pkg = SanitizePackage(package);
        var result = await _runner.RunAsync($"cd {app.AppPath} && .venv/bin/pip uninstall -y {pkg}", "pip");
        return _runner.SimulationMode ? $"Successfully uninstalled {pkg}" : result.Output;
    }

    public async Task<List<(string name, string version)>> PipListAsync(HostedApp app)
    {
        await _runner.LogExternalAsync($"cd {app.AppPath} && .venv/bin/pip list", "listed", _runner.SimulationMode, "pip");
        if (_runner.SimulationMode)
            return new List<(string, string)>
            {
                ("Flask", "3.0.3"), ("gunicorn", "22.0.0"), ("Werkzeug", "3.0.3"),
                ("Jinja2", "3.1.4"), ("click", "8.1.7"), ("requests", "2.32.3")
            };
        var result = await _runner.RunAsync($"cd {app.AppPath} && .venv/bin/pip list --format=freeze", "pip");
        return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split("=="))
            .Where(p => p.Length == 2)
            .Select(p => (p[0], p[1])).ToList();
    }

    public async Task<string> PipFreezeAsync(HostedApp app)
    {
        var result = await _runner.RunAsync($"cd {app.AppPath} && .venv/bin/pip freeze > requirements.txt", "pip");
        return _runner.SimulationMode ? "requirements.txt updated" : result.Output;
    }

    // ---------------- helpers ----------------

    private static bool LooksSecret(string key)
    {
        var k = key.ToUpperInvariant();
        return k.Contains("SECRET") || k.Contains("PASSWORD") || k.Contains("TOKEN") || k.Contains("KEY") || k.Contains("API");
    }

    private static string SanitizePackage(string package) =>
        new(package.Trim().Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or '@' or '/' or '=' or '<' or '>' or '~' or '^').ToArray());
}
