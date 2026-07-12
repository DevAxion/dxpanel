using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Renci.SshNet;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Nodes;

public record SshResult(bool Success, string Output, int ExitCode, long ElapsedMs, bool Simulated);

public record NodeMetrics(double CpuPercent, double RamPercent, double DiskPercent,
    double NetworkInMbps, double NetworkOutMbps, double Load1, double Load5, double Load15,
    int ActiveConnections, TimeSpan Uptime);

public record ProcessInfo(int Pid, string User, double CpuPercent, double MemPercent, string Command);

public record DiskUsage(string Mount, long TotalGB, long UsedGB, double UsedPercent);

/// <summary>
/// Runs commands on a remote node over SSH. Simulation-aware: on a dev host every call
/// is logged via ICommandRunner and returns deterministic/realistic fake output; on a real
/// host it connects with SSH.NET using the node's key or password.
/// </summary>
public interface INodeSshService
{
    Task<(bool ok, int latencyMs)> TestConnectionAsync(ServerNode node);
    Task<SshResult> ExecuteCommandAsync(ServerNode node, string command);
    Task<SshResult> RunScriptAsync(ServerNode node, string scriptContent);

    Task<ServerServiceStatus> GetServiceStatusAsync(ServerNode node, ServerServiceType service);
    Task<SshResult> RestartServiceAsync(ServerNode node, ServerServiceType service);
    Task<SshResult> ServiceActionAsync(ServerNode node, ServerServiceType service, string action);

    Task<NodeMetrics> GetMetricsAsync(ServerNode node);
    Task<List<ProcessInfo>> GetProcessListAsync(ServerNode node);
    Task<List<DiskUsage>> GetDiskUsageAsync(ServerNode node);

    Task<string> GetServiceLogAsync(ServerNode node, ServerServiceType service, int lines = 50);
    Task<SshResult> UploadFileAsync(ServerNode node, string localPath, string remotePath);
}

public class NodeSshService : INodeSshService
{
    private const string ServiceName = "node-ssh";

    private readonly ICommandRunner _runner;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<NodeSshService> _logger;

    public NodeSshService(ICommandRunner runner, ApplicationDbContext db, ILogger<NodeSshService> logger)
    {
        _runner = runner;
        _db = db;
        _logger = logger;
    }

    private bool Simulated => _runner.SimulationMode;

    public async Task<(bool ok, int latencyMs)> TestConnectionAsync(ServerNode node)
    {
        await _runner.LogExternalAsync($"ssh {node.SshUsername}@{node.IpAddress}:{node.SshPort} (test)",
            "connection ok", Simulated, ServiceName);

        if (Simulated)
            return (true, SeededRandom(node.IpAddress).Next(8, 40));

        try
        {
            var sw = Stopwatch.StartNew();
            using var client = CreateClient(node);
            client.Connect();
            var connected = client.IsConnected;
            client.Disconnect();
            sw.Stop();
            return (connected, (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SSH test to {Node} failed", node.Name);
            return (false, 0);
        }
    }

    public async Task<SshResult> ExecuteCommandAsync(ServerNode node, string command)
    {
        var sw = Stopwatch.StartNew();

        if (Simulated)
        {
            await _runner.LogExternalAsync($"ssh {node.Name}: {command}", SimulatedOutput(node, command), true, ServiceName);
            sw.Stop();
            return new SshResult(true, SimulatedOutput(node, command), 0, sw.ElapsedMilliseconds, true);
        }

        try
        {
            using var client = CreateClient(node);
            client.Connect();
            using var cmd = client.CreateCommand(command);
            var output = cmd.Execute();
            sw.Stop();
            client.Disconnect();

            await _runner.LogExternalAsync($"ssh {node.Name}: {command}", output, false, ServiceName, cmd.ExitStatus ?? 0);
            return new SshResult((cmd.ExitStatus ?? 0) == 0, output, cmd.ExitStatus ?? 0, sw.ElapsedMilliseconds, false);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "SSH command on {Node} failed", node.Name);
            return new SshResult(false, ex.Message, 1, sw.ElapsedMilliseconds, false);
        }
    }

    public Task<SshResult> RunScriptAsync(ServerNode node, string scriptContent) =>
        ExecuteCommandAsync(node, $"bash -c {Quote(scriptContent)}");

    public async Task<ServerServiceStatus> GetServiceStatusAsync(ServerNode node, ServerServiceType service)
    {
        var unit = UnitName(service);
        var result = await ExecuteCommandAsync(node, $"systemctl is-active {unit}");

        if (Simulated) return ServerServiceStatus.Running;

        return result.Output.Trim() switch
        {
            "active" => ServerServiceStatus.Running,
            "inactive" or "dead" => ServerServiceStatus.Stopped,
            "failed" => ServerServiceStatus.Error,
            _ => ServerServiceStatus.NotInstalled
        };
    }

    public Task<SshResult> RestartServiceAsync(ServerNode node, ServerServiceType service) =>
        ServiceActionAsync(node, service, "restart");

    public Task<SshResult> ServiceActionAsync(ServerNode node, ServerServiceType service, string action)
    {
        var allowed = new[] { "start", "stop", "restart", "reload" };
        if (!allowed.Contains(action)) throw new InvalidOperationException("Unsupported service action.");
        return ExecuteCommandAsync(node, $"systemctl {action} {UnitName(service)}");
    }

    public async Task<NodeMetrics> GetMetricsAsync(ServerNode node)
    {
        if (Simulated)
        {
            await _runner.LogExternalAsync($"ssh {node.Name}: collect metrics", "metrics collected", true, ServiceName);
            return SimulatedMetrics(node);
        }

        // Real: read /proc + df + ss in one round-trip and parse.
        var result = await ExecuteCommandAsync(node,
            "top -bn1 | grep 'Cpu(s)'; free | grep Mem; df -P / | tail -1; cat /proc/loadavg; ss -s | grep TCP; cat /proc/uptime");
        return ParseMetrics(result.Output, node);
    }

    public async Task<List<ProcessInfo>> GetProcessListAsync(ServerNode node)
    {
        if (Simulated) return SimulatedProcesses(node);

        var result = await ExecuteCommandAsync(node, "ps aux --sort=-%cpu | head -11");
        var processes = new List<ProcessInfo>();
        foreach (var line in result.Output.Split('\n').Skip(1))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 11) continue;
            if (double.TryParse(parts[2], out var cpu) && double.TryParse(parts[3], out var mem) && int.TryParse(parts[1], out var pid))
                processes.Add(new ProcessInfo(pid, parts[0], cpu, mem, string.Join(' ', parts.Skip(10)).Trim()));
        }
        return processes;
    }

    public async Task<List<DiskUsage>> GetDiskUsageAsync(ServerNode node)
    {
        if (Simulated) return SimulatedDisks(node);

        var result = await ExecuteCommandAsync(node, "df -PBG | grep '^/dev/'");
        var disks = new List<DiskUsage>();
        foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 6) continue;
            long.TryParse(parts[1].TrimEnd('G'), out var total);
            long.TryParse(parts[2].TrimEnd('G'), out var used);
            double.TryParse(parts[4].TrimEnd('%'), out var pct);
            disks.Add(new DiskUsage(parts[5], total, used, pct));
        }
        return disks;
    }

    public async Task<string> GetServiceLogAsync(ServerNode node, ServerServiceType service, int lines = 50)
    {
        if (Simulated) return SimulatedLog(node, service, lines);
        var result = await ExecuteCommandAsync(node, $"journalctl -u {UnitName(service)} -n {lines} --no-pager");
        return result.Output;
    }

    public async Task<SshResult> UploadFileAsync(ServerNode node, string localPath, string remotePath)
    {
        if (Simulated)
        {
            await _runner.LogExternalAsync($"scp {localPath} {node.Name}:{remotePath}", "uploaded", true, ServiceName);
            return new SshResult(true, "uploaded (simulated)", 0, 5, true);
        }

        try
        {
            using var client = CreateScpClient(node);
            client.Connect();
            await using var stream = File.OpenRead(localPath);
            client.Upload(stream, remotePath);
            client.Disconnect();
            return new SshResult(true, "uploaded", 0, 0, false);
        }
        catch (Exception ex)
        {
            return new SshResult(false, ex.Message, 1, 0, false);
        }
    }

    // ---------------- SSH.NET clients ----------------

    private SshClient CreateClient(ServerNode node) => new(BuildConnectionInfo(node));
    private ScpClient CreateScpClient(ServerNode node) => new(BuildConnectionInfo(node));

    private static Renci.SshNet.ConnectionInfo BuildConnectionInfo(ServerNode node)
    {
        AuthenticationMethod auth = node.UsesKeyAuth && File.Exists(node.SshKeyPath)
            ? new PrivateKeyAuthenticationMethod(node.SshUsername, new PrivateKeyFile(node.SshKeyPath))
            : new PasswordAuthenticationMethod(node.SshUsername, node.SshPassword ?? "");

        return new Renci.SshNet.ConnectionInfo(node.IpAddress, node.SshPort, node.SshUsername, auth)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    private static string UnitName(ServerServiceType service) => service switch
    {
        ServerServiceType.Nginx => "nginx",
        ServerServiceType.MySQL => "mysql",
        ServerServiceType.PHP => "php8.3-fpm",
        ServerServiceType.FTP => "vsftpd",
        ServerServiceType.Email => "postfix",
        ServerServiceType.DNS => "named",
        _ => "srx-backup"
    };

    private static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    // ---------------- Simulation ----------------

    private static Random SeededRandom(string seed) =>
        new(BitConverter.ToInt32(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed)), 0));

    /// <summary>Per-node metrics that vary each tick but stay in a plausible band for that node.</summary>
    public static NodeMetrics SimulatedMetrics(ServerNode node)
    {
        // Base band is seeded per node; a time-based jitter keeps it moving.
        var band = SeededRandom(node.IpAddress);
        var baseCpu = band.Next(15, 45);
        var baseRam = band.Next(40, 70);
        var baseDisk = band.Next(30, 60);

        var jitter = new Random();
        double Vary(double b, int spread) => Math.Clamp(b + jitter.Next(-spread, spread + 1), 1, 99);

        var cpu = Vary(baseCpu, 12);
        return new NodeMetrics(
            Math.Round(cpu, 1),
            Math.Round(Vary(baseRam, 8), 1),
            Math.Round(baseDisk + jitter.NextDouble(), 1),
            Math.Round(jitter.NextDouble() * 120, 1),
            Math.Round(jitter.NextDouble() * 80, 1),
            Math.Round(cpu / 100.0 * node.CpuCores * (0.8 + jitter.NextDouble() * 0.6), 2),
            Math.Round(cpu / 100.0 * node.CpuCores * 0.9, 2),
            Math.Round(cpu / 100.0 * node.CpuCores * 0.85, 2),
            jitter.Next(50, 1200),
            TimeSpan.FromHours(band.Next(200, 8000)));
    }

    private static List<ProcessInfo> SimulatedProcesses(ServerNode node)
    {
        var random = SeededRandom(node.IpAddress + "proc");
        (string user, string cmd)[] procs =
        {
            ("mysql", "/usr/sbin/mysqld"),
            ("www-data", "nginx: worker process"),
            ("www-data", "php-fpm: pool www"),
            ("root", "/usr/sbin/named -f -u bind"),
            ("root", "/usr/lib/postfix/sbin/master -w"),
            ("root", "/usr/sbin/vsftpd /etc/vsftpd.conf"),
            ("root", "/lib/systemd/systemd-journald"),
            ("redis", "redis-server 127.0.0.1:6379"),
            ("root", "sshd: [priv]"),
            ("root", "/usr/bin/containerd")
        };

        return procs.Select(p => new ProcessInfo(
                random.Next(400, 32000), p.user,
                Math.Round(random.NextDouble() * 25, 1),
                Math.Round(random.NextDouble() * 15, 1), p.cmd))
            .OrderByDescending(p => p.CpuPercent).ToList();
    }

    private static List<DiskUsage> SimulatedDisks(ServerNode node)
    {
        var random = SeededRandom(node.IpAddress + "disk");
        var rootUsed = random.Next(30, 60);
        var disks = new List<DiskUsage>
        {
            new("/", node.DiskGB, node.DiskGB * rootUsed / 100, rootUsed)
        };
        if (node.Type is NodeType.Storage or NodeType.Primary)
        {
            var dataUsed = random.Next(40, 75);
            disks.Add(new DiskUsage("/var/www", node.DiskGB * 4, node.DiskGB * 4 * dataUsed / 100, dataUsed));
        }
        return disks;
    }

    private static string SimulatedLog(ServerNode node, ServerServiceType service, int lines)
    {
        var now = DateTime.UtcNow;
        var samples = service switch
        {
            ServerServiceType.Nginx => new[] { "GET / HTTP/2.0 200", "GET /assets/app.css 304", "POST /api/v1/orders 201" },
            ServerServiceType.MySQL => new[] { "Query OK, 1 row affected", "Aborted connection (Got timeout reading)", "InnoDB: page cleaner" },
            ServerServiceType.DNS => new[] { "zone example.com/IN: loaded serial 2026071101", "client query: example.com IN A", "reloading configuration" },
            _ => new[] { "service tick ok", "reloaded configuration", "connection accepted" }
        };
        var random = SeededRandom(node.IpAddress + service);
        var sb = new System.Text.StringBuilder();
        for (var i = lines; i > 0; i--)
            sb.AppendLine($"{now.AddSeconds(-i * 7):MMM dd HH:mm:ss} {node.Hostname} {UnitName(service)}[{random.Next(400, 9999)}]: {samples[random.Next(samples.Length)]}");
        return sb.ToString();
    }

    private static string SimulatedOutput(ServerNode node, string command)
    {
        if (command.Contains("systemctl is-active")) return "active";
        if (command.Contains("systemctl restart") || command.Contains("systemctl start") || command.Contains("systemctl reload"))
            return "";
        if (command.Contains("uname")) return "Linux " + node.Hostname + " 6.8.0-40-generic x86_64 GNU/Linux";
        if (command.Contains("apt") && command.Contains("update"))
            return "Reading package lists... Done\nBuilding dependency tree... Done\n12 packages can be upgraded.";
        if (command.Contains("nginx -v")) return "nginx version: nginx/1.24.0";
        if (command.Contains("mysql --version")) return "mysql  Ver 8.0.39 for Linux on x86_64";
        if (command.Contains("rsync")) return "sent 1,234,567 bytes  received 4,096 bytes  total size 45,678,901";
        if (command.Contains("mysqldump")) return "-- Dump completed";
        return $"[simulated] {command}";
    }

    private static NodeMetrics ParseMetrics(string output, ServerNode node)
    {
        // Fallback to a simulated shape if parsing fails on an unexpected format.
        try
        {
            var lines = output.Split('\n');
            double cpu = 0, ram = 0, disk = 0, l1 = 0, l5 = 0, l15 = 0, uptime = 0;
            var conns = 0;

            foreach (var line in lines)
            {
                if (line.Contains("Cpu(s)"))
                {
                    var idle = ExtractNumber(line, "id");
                    cpu = Math.Round(100 - idle, 1);
                }
                else if (line.TrimStart().StartsWith("Mem"))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3 && double.TryParse(parts[1], out var total) && double.TryParse(parts[2], out var used) && total > 0)
                        ram = Math.Round(used / total * 100, 1);
                }
                else if (line.Contains('%') && line.Contains('/'))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var pctToken = parts.FirstOrDefault(p => p.EndsWith('%'));
                    if (pctToken != null) double.TryParse(pctToken.TrimEnd('%'), out disk);
                }
                else if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^\d+\.\d+ \d+\.\d+ \d+\.\d+"))
                {
                    var parts = line.Split(' ');
                    double.TryParse(parts[0], out l1);
                    double.TryParse(parts[1], out l5);
                    double.TryParse(parts[2], out l15);
                }
                else if (line.Contains("TCP:"))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"estab (\d+)");
                    if (m.Success) int.TryParse(m.Groups[1].Value, out conns);
                }
                else if (System.Text.RegularExpressions.Regex.IsMatch(line.Trim(), @"^\d+\.\d+ \d+\.\d+$"))
                {
                    double.TryParse(line.Split(' ')[0], out uptime);
                }
            }

            return new NodeMetrics(cpu, ram, disk, 0, 0, l1, l5, l15, conns, TimeSpan.FromSeconds(uptime));
        }
        catch
        {
            return SimulatedMetrics(node);
        }
    }

    private static double ExtractNumber(string line, string suffix)
    {
        var m = System.Text.RegularExpressions.Regex.Match(line, $@"(\d+[\.,]?\d*)\s*%?\s*{suffix}");
        return m.Success && double.TryParse(m.Groups[1].Value.Replace(',', '.'), out var v) ? v : 0;
    }
}
