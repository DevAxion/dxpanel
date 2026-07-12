using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.AppHosting;

public interface IRuntimeService
{
    Task<List<AppRuntime>> GetAvailableRuntimesAsync();
    Task<string> GetRuntimeVersionAsync(AppRuntimeType type);
    Task<List<AppRuntime>> GetInstalledVersionsAsync(AppRuntimeType type);

    IReadOnlyList<string> GetInstallableVersions(AppRuntimeType type);
    Task<AppRuntime> InstallRuntimeAsync(AppRuntimeType type, string version);
    Task RemoveRuntimeAsync(int runtimeId);
    Task SetDefaultAsync(int runtimeId);

    Task<string> GetActiveVersionAsync(AppRuntimeType type);
    Task<bool> CreateVirtualenvAsync(int appId, string pythonVersion);
}

/// <summary>
/// Manages installed language runtimes via nvm (Node), pyenv (Python), rbenv (Ruby) and go toolchains.
/// Simulation-safe: every install/switch is logged through ICommandRunner and the runtime table is the
/// source of truth for what is "installed".
/// </summary>
public class RuntimeService : IRuntimeService
{
    private const string ServiceName = "runtime";

    private readonly ApplicationDbContext _db;
    private readonly ICommandRunner _runner;

    public RuntimeService(ApplicationDbContext db, ICommandRunner runner)
    {
        _db = db;
        _runner = runner;
    }

    public Task<List<AppRuntime>> GetAvailableRuntimesAsync() =>
        _db.AppRuntimes.Where(r => r.IsActive).OrderBy(r => r.Type).ThenByDescending(r => r.Version).ToListAsync();

    public async Task<string> GetRuntimeVersionAsync(AppRuntimeType type)
    {
        var def = await _db.AppRuntimes.Where(r => r.Type == type && r.IsDefault).FirstOrDefaultAsync()
            ?? await _db.AppRuntimes.Where(r => r.Type == type).OrderByDescending(r => r.Version).FirstOrDefaultAsync();
        return def?.Version ?? "not installed";
    }

    public Task<List<AppRuntime>> GetInstalledVersionsAsync(AppRuntimeType type) =>
        _db.AppRuntimes.Where(r => r.Type == type).OrderByDescending(r => r.Version).ToListAsync();

    public IReadOnlyList<string> GetInstallableVersions(AppRuntimeType type) => type switch
    {
        AppRuntimeType.NodeJs => new[] { "22.4.0", "20.15.0", "18.20.3", "16.20.2" },
        AppRuntimeType.Python => new[] { "3.12.4", "3.11.9", "3.10.14", "3.9.19" },
        AppRuntimeType.Ruby => new[] { "3.3.3", "3.2.4", "3.1.5" },
        AppRuntimeType.Go => new[] { "1.22.4", "1.21.11", "1.20.14" },
        _ => Array.Empty<string>()
    };

    public async Task<AppRuntime> InstallRuntimeAsync(AppRuntimeType type, string version)
    {
        var command = type switch
        {
            AppRuntimeType.NodeJs => $"nvm install {version}",
            AppRuntimeType.Python => $"pyenv install {version}",
            AppRuntimeType.Ruby => $"rbenv install {version}",
            AppRuntimeType.Go => $"goenv install {version}",
            _ => $"install {type} {version}"
        };
        await _runner.RunAsync(command, ServiceName);

        var existing = await _db.AppRuntimes.FirstOrDefaultAsync(r => r.Type == type && r.Version == version);
        if (existing != null) { existing.IsActive = true; await _db.SaveChangesAsync(); return existing; }

        var runtime = new AppRuntime
        {
            Name = $"{type} {version}",
            Type = type,
            Version = version,
            BinaryPath = BinaryPath(type, version),
            IsActive = true,
            IsDefault = !await _db.AppRuntimes.AnyAsync(r => r.Type == type)
        };
        _db.AppRuntimes.Add(runtime);
        await _db.SaveChangesAsync();
        return runtime;
    }

    public async Task RemoveRuntimeAsync(int runtimeId)
    {
        var runtime = await _db.AppRuntimes.FindAsync(runtimeId);
        if (runtime == null) return;
        if (await _db.HostedApps.AnyAsync(a => a.RuntimeId == runtimeId))
            throw new InvalidOperationException("This runtime version is in use by an app.");

        var command = runtime.Type switch
        {
            AppRuntimeType.NodeJs => $"nvm uninstall {runtime.Version}",
            AppRuntimeType.Python => $"pyenv uninstall -f {runtime.Version}",
            _ => $"remove {runtime.Type} {runtime.Version}"
        };
        await _runner.RunAsync(command, ServiceName);
        _db.AppRuntimes.Remove(runtime);
        await _db.SaveChangesAsync();
    }

    public async Task SetDefaultAsync(int runtimeId)
    {
        var runtime = await _db.AppRuntimes.FindAsync(runtimeId);
        if (runtime == null) return;

        var command = runtime.Type switch
        {
            AppRuntimeType.NodeJs => $"nvm alias default {runtime.Version}",
            AppRuntimeType.Python => $"pyenv global {runtime.Version}",
            AppRuntimeType.Ruby => $"rbenv global {runtime.Version}",
            _ => $"set-default {runtime.Type} {runtime.Version}"
        };
        await _runner.RunAsync(command, ServiceName);

        var others = await _db.AppRuntimes.Where(r => r.Type == runtime.Type).ToListAsync();
        foreach (var r in others) r.IsDefault = r.Id == runtimeId;
        await _db.SaveChangesAsync();
    }

    public async Task<string> GetActiveVersionAsync(AppRuntimeType type)
    {
        var probe = type switch
        {
            AppRuntimeType.NodeJs => "node --version",
            AppRuntimeType.Python => "python --version",
            AppRuntimeType.Ruby => "ruby --version",
            AppRuntimeType.Go => "go version",
            _ => "--version"
        };
        if (_runner.SimulationMode)
        {
            await _runner.LogExternalAsync(probe, "version probed", true, ServiceName);
            return await GetRuntimeVersionAsync(type);
        }
        var result = await _runner.RunAsync(probe, ServiceName);
        return result.Output.Trim();
    }

    public async Task<bool> CreateVirtualenvAsync(int appId, string pythonVersion)
    {
        var app = await _db.HostedApps.FirstOrDefaultAsync(a => a.Id == appId);
        if (app == null) return false;

        await _runner.RunAsync($"cd {app.AppPath} && pyenv local {pythonVersion} && python -m venv .venv", ServiceName);
        app.VirtualenvCreated = true;
        app.PythonVersion = pythonVersion;
        await _db.SaveChangesAsync();
        return true;
    }

    private static string BinaryPath(AppRuntimeType type, string version) => type switch
    {
        AppRuntimeType.NodeJs => $"/root/.nvm/versions/node/v{version}/bin/node",
        AppRuntimeType.Python => $"/root/.pyenv/versions/{version}/bin/python",
        AppRuntimeType.Ruby => $"/root/.rbenv/versions/{version}/bin/ruby",
        AppRuntimeType.Go => $"/usr/local/go/{version}/bin/go",
        _ => $"/usr/bin/{type}".ToLowerInvariant()
    };
}
