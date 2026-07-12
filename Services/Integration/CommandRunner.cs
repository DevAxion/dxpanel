using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Http;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Integration;

/// <summary>
/// Shell command runner. Honors SimulationMode: when on (default on Windows/dev),
/// commands and file writes are logged to CommandLog with a "[SIMULATED]" marker
/// and the intended action, but nothing touches the real filesystem or services.
/// On Linux production with SimulationMode=false, commands run via /bin/bash -c.
/// </summary>
public class CommandRunner : ICommandRunner
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<CommandRunner> _logger;

    public bool SimulationMode { get; }

    public CommandRunner(ApplicationDbContext db, IHttpContextAccessor http,
        IConfiguration config, ILogger<CommandRunner> logger)
    {
        _db = db;
        _http = http;
        _logger = logger;

        // Explicit config wins; otherwise simulate unless we're on Linux.
        var configured = config.GetValue<bool?>("SimulationMode");
        SimulationMode = configured ?? !RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    }

    public async Task<CommandResult> RunAsync(string command, string? service = null)
    {
        if (SimulationMode)
        {
            return await LogAsync(command, $"[SIMULATED] would execute:\n$ {command}", 0, true, service);
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return await LogAsync(command, "Failed to start process.", -1, false, service);
            }

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var output = string.Join("\n", new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return await LogAsync(command, output, process.ExitCode, false, service);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command failed: {Command}", command);
            return await LogAsync(command, $"EXCEPTION: {ex.Message}", -1, false, service);
        }
    }

    public Task<CommandResult> LogExternalAsync(string action, string output, bool simulated, string? service = null, int exitCode = 0)
    {
        var prefix = simulated ? "[SIMULATED] " : string.Empty;
        return LogAsync(action, prefix + output, exitCode, simulated, service);
    }

    public async Task<CommandResult> WriteFileAsync(string path, string content, string? service = null)
    {
        var label = $"write file {path} ({content.Length} bytes)";
        if (SimulationMode)
        {
            var preview = content.Length > 4000 ? content[..4000] + "\n... (truncated)" : content;
            return await LogAsync(label, $"[SIMULATED] would write to {path}:\n{preview}", 0, true, service);
        }

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(path, content);
            return await LogAsync(label, $"Wrote {content.Length} bytes to {path}.", 0, false, service);
        }
        catch (Exception ex)
        {
            return await LogAsync(label, $"EXCEPTION: {ex.Message}", -1, false, service);
        }
    }

    public async Task<CommandResult> DeleteFileAsync(string path, string? service = null)
    {
        var label = $"delete file {path}";
        if (SimulationMode)
        {
            return await LogAsync(label, $"[SIMULATED] would delete file {path}", 0, true, service);
        }

        try
        {
            if (File.Exists(path)) File.Delete(path);
            return await LogAsync(label, $"Deleted {path}.", 0, false, service);
        }
        catch (Exception ex)
        {
            return await LogAsync(label, $"EXCEPTION: {ex.Message}", -1, false, service);
        }
    }

    public async Task<CommandResult> DeletePathAsync(string path, string? service = null)
    {
        var label = $"delete path {path}";
        if (SimulationMode)
        {
            return await LogAsync(label, $"[SIMULATED] would recursively delete {path}", 0, true, service);
        }

        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
            return await LogAsync(label, $"Deleted {path}.", 0, false, service);
        }
        catch (Exception ex)
        {
            return await LogAsync(label, $"EXCEPTION: {ex.Message}", -1, false, service);
        }
    }

    public async Task<CommandResult> CreateSymlinkAsync(string target, string linkPath, string? service = null)
    {
        var label = $"symlink {linkPath} -> {target}";
        if (SimulationMode)
        {
            return await LogAsync(label, $"[SIMULATED] would create symlink {linkPath} -> {target}", 0, true, service);
        }

        try
        {
            if (File.Exists(linkPath) || Directory.Exists(linkPath)) File.Delete(linkPath);
            File.CreateSymbolicLink(linkPath, target);
            return await LogAsync(label, $"Linked {linkPath} -> {target}.", 0, false, service);
        }
        catch (Exception ex)
        {
            return await LogAsync(label, $"EXCEPTION: {ex.Message}", -1, false, service);
        }
    }

    private async Task<CommandResult> LogAsync(string command, string output, int exitCode, bool simulated, string? service)
    {
        var triggeredBy = _http.HttpContext?.User?.Identity?.Name ?? "system";

        _db.CommandLogs.Add(new CommandLog
        {
            Command = command,
            Output = output,
            ExitCode = exitCode,
            Simulated = simulated,
            Service = service,
            TriggeredBy = triggeredBy,
            ExecutedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return new CommandResult
        {
            Command = command,
            Output = output,
            ExitCode = exitCode,
            Simulated = simulated
        };
    }
}
