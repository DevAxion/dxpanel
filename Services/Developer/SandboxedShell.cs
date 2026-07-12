using System.Text;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Developer;

/// <summary>
/// The shell that backs the browser terminal.
/// <para>
/// On Linux with SimulationMode off, commands are handed to <see cref="ICommandRunner"/>,
/// which runs them as a shell command. On a dev host (or in simulation) a small built-in
/// shell answers instead, reading real data from the user's sandboxed home via
/// <see cref="IFileManagerService"/> so the terminal is useful rather than a stub.
/// </para>
/// Either way the working directory is confined to the user's home tree.
/// </summary>
public class SandboxedShell
{
    private readonly IFileManagerService _files;
    private readonly ICommandRunner _runner;
    private readonly string _userId;
    private readonly string _userName;

    /// <summary>Current directory, relative to the sandbox root ("" is the home directory).</summary>
    private string _cwd = "";

    public SandboxedShell(IFileManagerService files, ICommandRunner runner, string userId, string userName)
    {
        _files = files;
        _runner = runner;
        _userId = userId;
        _userName = userName;
        _files.EnsureUserRoot(userId);
    }

    public string Prompt => $"\u001b[32m{_userName}@srxpanel\u001b[0m:\u001b[34m~/{_cwd}\u001b[0m$ ";

    public string Banner =>
        "\u001b[36mSRXPanel Web Terminal\u001b[0m\r\n" +
        $"Connected as \u001b[32m{_userName}\u001b[0m. Your session is confined to your home directory.\r\n" +
        (_runner.SimulationMode
            ? "\u001b[33mSimulation mode:\u001b[0m a built-in shell is answering. File commands read your real sandboxed files.\r\n"
            : "") +
        "Type \u001b[1mhelp\u001b[0m for the available commands.\r\n\r\n";

    /// <summary>Runs one command line and returns its output (already CRLF terminated).</summary>
    public async Task<string> ExecuteAsync(string commandLine)
    {
        var line = commandLine.Trim();
        if (line.Length == 0) return "";

        // A real Linux host executes the command for real; sandboxing is enforced by the OS user.
        if (!_runner.SimulationMode && OperatingSystem.IsLinux())
        {
            var home = $"/home/{_userName}";
            var target = string.IsNullOrEmpty(_cwd) ? home : $"{home}/{_cwd}";
            var result = await _runner.RunAsync($"cd {target} && {line}", "terminal");
            return Normalize(result.Output);
        }

        return await BuiltInAsync(line);
    }

    private async Task<string> BuiltInAsync(string line)
    {
        var parts = SplitArgs(line);
        var command = parts[0];
        var args = parts.Skip(1).ToArray();

        switch (command)
        {
            case "help":
                return Normalize(
                    "Available commands:\r\n" +
                    "  ls [path]        list directory contents\r\n" +
                    "  cd <path>        change directory\r\n" +
                    "  pwd              print working directory\r\n" +
                    "  cat <file>       print a text file\r\n" +
                    "  mkdir <name>     create a directory\r\n" +
                    "  touch <name>     create an empty file\r\n" +
                    "  rm <path>        delete a file or directory\r\n" +
                    "  du               show disk usage for your account\r\n" +
                    "  echo <text>      print text\r\n" +
                    "  whoami           print the current user\r\n" +
                    "  date             print the current UTC time\r\n" +
                    "  uname [-a]       print system information\r\n" +
                    "  php|node|npm|composer|git --version\r\n" +
                    "  clear            clear the screen\r\n" +
                    "  exit             close the session");

            case "pwd":
                return Normalize($"/home/{_userName}/{_cwd}".TrimEnd('/'));

            case "whoami":
                return Normalize(_userName);

            case "date":
                return Normalize(DateTime.UtcNow.ToString("ddd MMM d HH:mm:ss 'UTC' yyyy"));

            case "uname":
                return Normalize(args.Contains("-a")
                    ? "Linux srxpanel 6.1.0-18-amd64 #1 SMP Debian 6.1.76-1 x86_64 GNU/Linux"
                    : "Linux");

            case "echo":
                return Normalize(string.Join(' ', args).Trim('"', '\''));

            case "clear":
                return "\u001b[2J\u001b[H";

            case "exit":
            case "logout":
                return ""; // EOT — the socket loop closes on this.

            case "ls":
                return Ls(args);

            case "cd":
                return Cd(args.FirstOrDefault());

            case "cat":
                return Cat(args.FirstOrDefault());

            case "mkdir":
                return Mkdir(args.FirstOrDefault());

            case "touch":
                return Touch(args.FirstOrDefault());

            case "rm":
                return Rm(args.LastOrDefault());

            case "du":
                var bytes = _files.GetUsedBytes(_userId);
                return Normalize($"{bytes / 1024.0:0.0}K\t/home/{_userName}");

            case "php":
                return Normalize(VersionFlag(args)
                    ? "PHP 8.3.6 (cli) (built: Mar 21 2026 12:00:00) (NTS)\r\nZend Engine v4.3.6, Copyright (c) Zend Technologies"
                    : "Usage: php --version");

            case "node":
                return Normalize(VersionFlag(args) ? "v20.11.1" : "Usage: node --version");

            case "npm":
                return Normalize(VersionFlag(args) ? "10.2.4" : "Usage: npm --version");

            case "composer":
                return Normalize(VersionFlag(args) ? "Composer version 2.7.2 2026-03-11 10:00:00" : "Usage: composer --version");

            case "git":
                return Normalize(VersionFlag(args) ? "git version 2.43.0" : "Usage: git --version");

            case "sudo":
            case "su":
                return Normalize($"\u001b[31m{command}: permission denied — terminal sessions never run as root.\u001b[0m");

            default:
                // Log the attempt so the command history is auditable, then report it like a shell would.
                await _runner.LogExternalAsync($"terminal: {line}", "command not found", true, "terminal", 127);
                return Normalize($"\u001b[31m{command}: command not found\u001b[0m");
        }
    }

    private static bool VersionFlag(string[] args) =>
        args.Any(a => a is "-v" or "-V" or "--version");

    private string Ls(string[] args)
    {
        var path = args.FirstOrDefault(a => !a.StartsWith('-')) ?? "";
        var showAll = args.Any(a => a.StartsWith('-') && a.Contains('a'));
        var longFormat = args.Any(a => a.StartsWith('-') && a.Contains('l'));

        try
        {
            var target = Resolve(path);
            var entries = _files.List(_userId, target)
                .Where(e => showAll || !e.Name.StartsWith('.'))
                .ToList();

            if (entries.Count == 0) return "";

            if (!longFormat)
            {
                var names = entries.Select(e => e.IsDirectory ? $"\u001b[34m{e.Name}\u001b[0m" : e.Name);
                return Normalize(string.Join("  ", names));
            }

            var sb = new StringBuilder();
            foreach (var entry in entries)
            {
                var kind = entry.IsDirectory ? "d" : "-";
                var size = entry.IsDirectory ? "-" : entry.Size.ToString();
                var name = entry.IsDirectory ? $"\u001b[34m{entry.Name}\u001b[0m" : entry.Name;
                sb.Append($"{kind}rw-r--r--  {_userName,-10} {size,10}  {entry.Modified:MMM dd HH:mm}  {name}\r\n");
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return Normalize($"ls: {ex.Message}");
        }
    }

    private string Cd(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "~")
        {
            _cwd = "";
            return "";
        }

        try
        {
            var target = Resolve(path);
            // List() throws when the path is missing or escapes the sandbox.
            _files.List(_userId, target);
            _cwd = target;
            return "";
        }
        catch (Exception ex)
        {
            return Normalize($"cd: {path}: {ex.Message}");
        }
    }

    private string Cat(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Normalize("cat: missing file operand");
        try
        {
            var content = _files.ReadTextFile(_userId, Resolve(path), out var editable);
            if (!editable) return Normalize($"cat: {path}: binary file");
            return Normalize(content);
        }
        catch (Exception ex)
        {
            return Normalize($"cat: {path}: {ex.Message}");
        }
    }

    private string Mkdir(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Normalize("mkdir: missing operand");
        try
        {
            _files.CreateDirectory(_userId, _cwd, name);
            return "";
        }
        catch (Exception ex)
        {
            return Normalize($"mkdir: {ex.Message}");
        }
    }

    private string Touch(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Normalize("touch: missing operand");
        try
        {
            _files.CreateFile(_userId, _cwd, name);
            return "";
        }
        catch (Exception ex)
        {
            return Normalize($"touch: {ex.Message}");
        }
    }

    private string Rm(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('-')) return Normalize("rm: missing operand");
        try
        {
            _files.Delete(_userId, Resolve(path));
            return "";
        }
        catch (Exception ex)
        {
            return Normalize($"rm: {path}: {ex.Message}");
        }
    }

    /// <summary>Resolves a shell path against the current directory, staying inside the sandbox.</summary>
    private string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == ".") return _cwd;

        var p = path.Replace('\\', '/').Trim();
        if (p.StartsWith('~')) p = p.TrimStart('~').TrimStart('/');

        var segments = (p.StartsWith('/') ? p : $"{_cwd}/{p}")
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        var stack = new List<string>();
        foreach (var segment in segments)
        {
            if (segment == ".") continue;
            if (segment == "..")
            {
                // Popping past the root simply keeps you at the root, as a chroot would.
                if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                continue;
            }
            stack.Add(segment);
        }
        return string.Join('/', stack);
    }

    /// <summary>xterm.js needs CRLF line endings.</summary>
    private static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var normalized = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
        return normalized.EndsWith("\r\n") ? normalized : normalized + "\r\n";
    }

    /// <summary>Splits a command line, honouring single and double quotes.</summary>
    private static string[] SplitArgs(string line)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        char? quote = null;

        foreach (var c in line)
        {
            if (quote.HasValue)
            {
                if (c == quote) quote = null;
                else current.Append(c);
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (current.Length > 0) { args.Add(current.ToString()); current.Clear(); }
            }
            else
            {
                current.Append(c);
            }
        }
        if (current.Length > 0) args.Add(current.ToString());
        return args.Count == 0 ? new[] { "" } : args.ToArray();
    }
}
