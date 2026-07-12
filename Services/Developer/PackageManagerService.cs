using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Developer;

public enum PackageRunner
{
    Composer,
    Npm,
    Pip
}

public record PackageFile(string Name, string Content, bool Exists);

public interface IPackageManagerService
{
    /// <summary>Directories under the user's home that contain a manifest for the given runner.</summary>
    IReadOnlyList<string> GetWorkingDirectories(string userId, PackageRunner runner);

    /// <summary>Reads composer.json / package.json / requirements.txt from a working directory.</summary>
    PackageFile ReadManifest(string userId, string workingDir, PackageRunner runner);
    void WriteManifest(string userId, string workingDir, PackageRunner runner, string content);

    /// <summary>Validates a command against the runner's allow-list.</summary>
    (bool ok, string error) Validate(PackageRunner runner, string command);

    /// <summary>Starts a command and streams its output over SignalR. Returns the runner id.</summary>
    Task<string> RunAsync(string userId, string workingDir, PackageRunner runner, string command);

    /// <summary>Total size of node_modules / vendor in the working directory.</summary>
    long GetDependencySize(string userId, string workingDir, PackageRunner runner);

    IReadOnlyList<string> GetNodeVersions();
    IReadOnlyList<string> GetNpmScripts(string userId, string workingDir);
}

public class PackageManagerService : IPackageManagerService
{
    private const string ServiceName = "package-manager";

    // Only these sub-commands may run. Anything else is rejected before it reaches a shell.
    private static readonly Dictionary<PackageRunner, string[]> AllowedCommands = new()
    {
        [PackageRunner.Composer] = new[]
        {
            "install", "update", "require", "remove", "dump-autoload", "clear-cache",
            "show", "validate", "outdated", "audit"
        },
        [PackageRunner.Npm] = new[]
        {
            "install", "ci", "update", "run", "audit", "outdated", "list", "prune", "build"
        },
        [PackageRunner.Pip] = new[]
        {
            "install", "list", "freeze", "uninstall", "show", "check"
        }
    };

    /// <summary>A package name/script argument: no shell metacharacters, no path traversal.</summary>
    private static readonly Regex SafeArgument = new(@"^[A-Za-z0-9._/@:^~<>=\-\+\[\]]+$", RegexOptions.Compiled);

    private readonly IFileManagerService _files;
    private readonly IServiceScopeFactory _scopeFactory;

    public PackageManagerService(IFileManagerService files, IServiceScopeFactory scopeFactory)
    {
        _files = files;
        _scopeFactory = scopeFactory;
    }

    private static string ManifestName(PackageRunner runner) => runner switch
    {
        PackageRunner.Composer => "composer.json",
        PackageRunner.Npm => "package.json",
        _ => "requirements.txt"
    };

    private static string Binary(PackageRunner runner) => runner switch
    {
        PackageRunner.Composer => "composer",
        PackageRunner.Npm => "npm",
        _ => "pip"
    };

    public IReadOnlyList<string> GetWorkingDirectories(string userId, PackageRunner runner)
    {
        var root = _files.EnsureUserRoot(userId);
        var manifest = ManifestName(runner);

        var directories = new List<string> { "/" };
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            {
                // node_modules and vendor are dependency trees, not project roots.
                var relative = Path.GetRelativePath(root, dir).Replace('\\', '/');
                if (relative.Contains("node_modules") || relative.Contains("vendor") || relative.StartsWith('.')) continue;
                if (relative.Count(c => c == '/') > 3) continue;
                directories.Add("/" + relative);
            }
        }
        catch (DirectoryNotFoundException)
        {
            // Fresh account with no home directory yet.
        }

        // Surface directories holding a manifest first — those are the interesting ones.
        return directories
            .OrderByDescending(d => File.Exists(Path.Combine(root, d.TrimStart('/'), manifest)))
            .ThenBy(d => d)
            .ToList();
    }

    public PackageFile ReadManifest(string userId, string workingDir, PackageRunner runner)
    {
        var name = ManifestName(runner);
        var relative = CombineRelative(workingDir, name);

        try
        {
            var content = _files.ReadTextFile(userId, relative, out _);
            return new PackageFile(name, content, true);
        }
        catch (Exception)
        {
            return new PackageFile(name, DefaultManifest(runner), false);
        }
    }

    public void WriteManifest(string userId, string workingDir, PackageRunner runner, string content)
    {
        var name = ManifestName(runner);
        var relative = CombineRelative(workingDir, name);

        // CreateFile is a no-op when the file already exists; WriteTextFile needs it to exist.
        try { _files.CreateFile(userId, workingDir.Trim('/'), name); } catch (FileManagerException) { }
        _files.WriteTextFile(userId, relative, content);
    }

    public (bool ok, string error) Validate(PackageRunner runner, string command)
    {
        var trimmed = (command ?? "").Trim();
        if (trimmed.Length == 0) return (false, "Enter a command.");
        if (trimmed.Length > 200) return (false, "Command is too long.");

        // Reject anything that could chain a second command.
        foreach (var c in new[] { ';', '|', '&', '`', '$', '>', '<', '\n' })
            if (trimmed.Contains(c)) return (false, $"'{c}' is not allowed in package-manager commands.");

        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Accept both "install" and "composer install" / "npm install".
        var offset = parts[0].Equals(Binary(runner), StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (parts.Length <= offset) return (false, "Enter a command.");

        var subCommand = parts[offset].ToLowerInvariant();
        if (!AllowedCommands[runner].Contains(subCommand))
            return (false, $"'{subCommand}' is not on the allow-list for {Binary(runner)}. Allowed: {string.Join(", ", AllowedCommands[runner])}.");

        foreach (var argument in parts.Skip(offset + 1))
        {
            if (argument.StartsWith("--"))
            {
                if (!SafeArgument.IsMatch(argument.TrimStart('-'))) return (false, $"Invalid flag '{argument}'.");
                continue;
            }
            if (!SafeArgument.IsMatch(argument)) return (false, $"Invalid argument '{argument}'.");
            if (argument.Contains("..")) return (false, "Paths containing '..' are not allowed.");
        }

        return (true, "");
    }

    public Task<string> RunAsync(string userId, string workingDir, PackageRunner runner, string command)
    {
        var (ok, error) = Validate(runner, command);
        if (!ok) throw new InvalidOperationException(error);

        var normalized = command.Trim();
        if (!normalized.StartsWith(Binary(runner), StringComparison.OrdinalIgnoreCase))
            normalized = $"{Binary(runner)} {normalized}";

        var runnerId = Guid.NewGuid().ToString("N")[..12];

        // The command runs on a background task with its own scope; output streams over SignalR.
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            await ExecuteAsync(scope.ServiceProvider, runnerId, userId, workingDir, runner, normalized);
        });

        return Task.FromResult(runnerId);
    }

    private static async Task ExecuteAsync(IServiceProvider sp, string runnerId, string userId, string workingDir,
        PackageRunner runner, string command)
    {
        var broadcast = sp.GetRequiredService<IDevToolsBroadcast>();
        var commandRunner = sp.GetRequiredService<ICommandRunner>();
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var logger = sp.GetRequiredService<ILogger<PackageManagerService>>();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        var home = $"/home/{user?.UserName ?? "user"}";
        var target = $"{home}{(workingDir == "/" ? "" : workingDir)}";

        try
        {
            await broadcast.RunnerOutputAsync(runnerId, $"$ cd {target}");
            await broadcast.RunnerOutputAsync(runnerId, $"$ {command}");

            var result = await commandRunner.RunAsync($"cd {target} && {command}", ServiceName);

            if (result.Simulated)
            {
                foreach (var line in SimulatedOutput(runner, command))
                {
                    await broadcast.RunnerOutputAsync(runnerId, line);
                    await Task.Delay(220);
                }
                await broadcast.RunnerCompletedAsync(runnerId, 0);
                return;
            }

            foreach (var line in result.Output.Split('\n'))
                await broadcast.RunnerOutputAsync(runnerId, line.TrimEnd('\r'));

            await broadcast.RunnerCompletedAsync(runnerId, result.ExitCode);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Package manager run {RunnerId} failed", runnerId);
            await broadcast.RunnerOutputAsync(runnerId, $"ERROR: {ex.Message}");
            await broadcast.RunnerCompletedAsync(runnerId, 1);
        }
    }

    /// <summary>Realistic output for the dev host, where no package manager is actually installed.</summary>
    private static string[] SimulatedOutput(PackageRunner runner, string command) => runner switch
    {
        PackageRunner.Composer when command.Contains("install") => new[]
        {
            "Loading composer repositories with package information",
            "Updating dependencies",
            "Lock file operations: 0 installs, 0 updates, 0 removals",
            "Installing dependencies from lock file (including require-dev)",
            "Package operations: 58 installs, 0 updates, 0 removals",
            "  - Downloading symfony/console (v7.0.4)",
            "  - Downloading monolog/monolog (3.5.0)",
            "  - Installing symfony/console (v7.0.4): Extracting archive",
            "  - Installing monolog/monolog (3.5.0): Extracting archive",
            "Generating optimized autoload files",
            "58 packages you are using are looking for funding.",
            "No security vulnerability advisories found."
        },
        PackageRunner.Composer when command.Contains("dump-autoload") => new[]
        {
            "Generating optimized autoload files",
            "Generated optimized autoload files containing 1842 classes"
        },
        PackageRunner.Composer when command.Contains("clear-cache") => new[]
        {
            "Clearing cache (cache-vcs-dir): /home/user/.cache/composer/vcs",
            "Clearing cache (cache-repo-dir): /home/user/.cache/composer/repo",
            "All caches cleared."
        },
        PackageRunner.Composer => new[]
        {
            "Loading composer repositories with package information",
            "Updating dependencies",
            "Nothing to modify in lock file",
            "Generating optimized autoload files"
        },

        PackageRunner.Npm when command.Contains("run build") || command.Contains("npm build") => new[]
        {
            "> build",
            "> vite build",
            "",
            "vite v5.2.0 building for production...",
            "transforming...",
            "✓ 384 modules transformed.",
            "dist/index.html                   0.46 kB │ gzip:  0.30 kB",
            "dist/assets/index-4f2a91c8.css   14.21 kB │ gzip:  3.11 kB",
            "dist/assets/index-9b1d7e02.js   142.87 kB │ gzip: 46.02 kB",
            "✓ built in 4.21s"
        },
        PackageRunner.Npm when command.Contains("audit") => new[]
        {
            "# npm audit report",
            "",
            "found 0 vulnerabilities"
        },
        PackageRunner.Npm => new[]
        {
            "npm warn deprecated inflight@1.0.6: This module is not supported",
            "",
            "added 612 packages, and audited 613 packages in 9s",
            "",
            "84 packages are looking for funding",
            "  run `npm fund` for details",
            "",
            "found 0 vulnerabilities"
        },

        PackageRunner.Pip when command.Contains("list") => new[]
        {
            "Package    Version",
            "---------- -------",
            "certifi    2026.1.14",
            "Django     5.0.3",
            "gunicorn   21.2.0",
            "pip        24.0",
            "requests   2.31.0"
        },
        _ => new[]
        {
            "Collecting requests",
            "  Downloading requests-2.31.0-py3-none-any.whl (62 kB)",
            "Installing collected packages: requests",
            "Successfully installed requests-2.31.0"
        }
    };

    public long GetDependencySize(string userId, string workingDir, PackageRunner runner)
    {
        var folder = runner switch
        {
            PackageRunner.Composer => "vendor",
            PackageRunner.Npm => "node_modules",
            _ => ".venv"
        };

        var root = _files.EnsureUserRoot(userId);
        var path = Path.Combine(root, workingDir.Trim('/').Replace('/', Path.DirectorySeparatorChar), folder);
        if (!Directory.Exists(path)) return 0;

        try
        {
            return new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    public IReadOnlyList<string> GetNodeVersions() => new[] { "20.11.1 (LTS)", "18.19.1", "22.0.0" };

    public IReadOnlyList<string> GetNpmScripts(string userId, string workingDir)
    {
        var manifest = ReadManifest(userId, workingDir, PackageRunner.Npm);
        if (!manifest.Exists) return Array.Empty<string>();

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(manifest.Content);
            if (!document.RootElement.TryGetProperty("scripts", out var scripts)) return Array.Empty<string>();
            return scripts.EnumerateObject().Select(p => p.Name).ToList();
        }
        catch (System.Text.Json.JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static string CombineRelative(string workingDir, string fileName)
    {
        var dir = workingDir.Trim('/');
        return string.IsNullOrEmpty(dir) ? fileName : $"{dir}/{fileName}";
    }

    private static string DefaultManifest(PackageRunner runner) => runner switch
    {
        PackageRunner.Composer =>
            "{\n  \"name\": \"vendor/project\",\n  \"require\": {\n    \"php\": \">=8.1\"\n  }\n}\n",
        PackageRunner.Npm =>
            "{\n  \"name\": \"project\",\n  \"version\": \"1.0.0\",\n  \"scripts\": {\n    \"build\": \"vite build\",\n    \"dev\": \"vite\"\n  }\n}\n",
        _ => "# Add one package per line, e.g.\n# requests==2.31.0\n"
    };
}
