namespace SRXPanel.Services.Interfaces;

/// <summary>Result of a single shell command (or simulated command).</summary>
public class CommandResult
{
    public string Command { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
    public int ExitCode { get; set; }
    public bool Simulated { get; set; }
    public bool Success => ExitCode == 0;
}

/// <summary>Aggregated result of a service operation that runs one or more commands.</summary>
public class ServiceResult
{
    public bool Success { get; set; } = true;
    public string Message { get; set; } = string.Empty;
    public List<CommandResult> Commands { get; set; } = new();

    public bool Simulated => Commands.Count > 0 && Commands.TrueForAll(c => c.Simulated);

    public static ServiceResult Ok(string message, params CommandResult[] commands) =>
        new() { Success = true, Message = message, Commands = commands.ToList() };

    public static ServiceResult Fail(string message, params CommandResult[] commands) =>
        new() { Success = false, Message = message, Commands = commands.ToList() };
}

/// <summary>
/// Executes shell commands and file operations against the host.
/// In simulation mode (Windows/dev) commands are logged but NOT executed.
/// Every command is recorded in the CommandLog table.
/// </summary>
public interface ICommandRunner
{
    bool SimulationMode { get; }

    Task<CommandResult> RunAsync(string command, string? service = null);

    /// <summary>Records an external API call (e.g. Stripe) or email send in the CommandLog without executing a shell command.</summary>
    Task<CommandResult> LogExternalAsync(string action, string output, bool simulated, string? service = null, int exitCode = 0);

    Task<CommandResult> WriteFileAsync(string path, string content, string? service = null);
    Task<CommandResult> DeleteFileAsync(string path, string? service = null);
    Task<CommandResult> DeletePathAsync(string path, string? service = null);
    Task<CommandResult> CreateSymlinkAsync(string target, string linkPath, string? service = null);
}
