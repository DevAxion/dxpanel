using System.Text;
using System.Text.Json;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.AppHosting;

public record ProcMetrics(string Status, long UptimeSeconds, double CpuPercent, double MemoryMB, int Pid, int Restarts);

public record ProcResult(bool Success, string Output, int? Pm2Id = null);

/// <summary>
/// Drives PM2 for Node.js apps. Simulation-safe: every action is logged through ICommandRunner and
/// returns realistic fake process metrics; a real host runs the corresponding pm2 command.
/// </summary>
public interface IPm2Service
{
    Task<ProcResult> StartAppAsync(HostedApp app);
    Task<ProcResult> StopAppAsync(int pm2Id);
    Task<ProcResult> RestartAppAsync(int pm2Id);
    Task<ProcResult> ReloadAppAsync(int pm2Id);
    Task<ProcResult> DeleteAppAsync(int pm2Id);
    Task<ProcResult> ScaleAppAsync(int pm2Id, int instances);
    Task<ProcMetrics> GetStatusAsync(HostedApp app);
    Task<(string stdout, string stderr)> GetLogsAsync(HostedApp app, int lines = 200);
    Task<ProcResult> FlushLogsAsync(int pm2Id);
    Task<ProcResult> SavePm2ListAsync();
    Task<string> GetAllAppsAsync();

    string GenerateEcosystem(HostedApp app, IEnumerable<HostedAppEnv> env);
}

public class Pm2Service : IPm2Service
{
    private const string ServiceName = "pm2";
    private static int _fakePm2Counter = 0;

    private readonly ICommandRunner _runner;

    public Pm2Service(ICommandRunner runner) => _runner = runner;

    private bool Sim => _runner.SimulationMode;

    public async Task<ProcResult> StartAppAsync(HostedApp app)
    {
        var config = GenerateEcosystem(app, app.EnvVars);
        if (Sim)
        {
            await _runner.LogExternalAsync($"pm2 start ecosystem.config.js ({app.Name})",
                $"Would run: pm2 start {app.Name} on port {app.Port}\n{config}", true, ServiceName);
            var pm2Id = System.Threading.Interlocked.Increment(ref _fakePm2Counter);
            return new ProcResult(true, $"[PM2] App {app.Name} started (id {pm2Id})", pm2Id);
        }

        await _runner.WriteFileAsync($"{app.AppPath}/ecosystem.config.js", config, ServiceName);
        var result = await _runner.RunAsync($"cd {app.AppPath} && pm2 start ecosystem.config.js", ServiceName);
        return new ProcResult(result.Success, result.Output);
    }

    public Task<ProcResult> StopAppAsync(int pm2Id) => ActionAsync(pm2Id, "stop");
    public Task<ProcResult> RestartAppAsync(int pm2Id) => ActionAsync(pm2Id, "restart");
    public Task<ProcResult> ReloadAppAsync(int pm2Id) => ActionAsync(pm2Id, "reload");
    public Task<ProcResult> DeleteAppAsync(int pm2Id) => ActionAsync(pm2Id, "delete");

    private async Task<ProcResult> ActionAsync(int pm2Id, string action)
    {
        var result = await _runner.RunAsync($"pm2 {action} {pm2Id}", ServiceName);
        return new ProcResult(result.Success, result.Output, pm2Id);
    }

    public async Task<ProcResult> ScaleAppAsync(int pm2Id, int instances)
    {
        instances = Math.Clamp(instances, 1, 4);
        var result = await _runner.RunAsync($"pm2 scale {pm2Id} {instances}", ServiceName);
        return new ProcResult(result.Success, result.Output, pm2Id);
    }

    public async Task<ProcMetrics> GetStatusAsync(HostedApp app)
    {
        if (Sim)
        {
            await _runner.LogExternalAsync($"pm2 jlist ({app.Name})", "status probed", true, ServiceName);
            return SimulatedMetrics(app);
        }
        var result = await _runner.RunAsync($"pm2 jlist", ServiceName);
        return ParseMetrics(result.Output, app);
    }

    public async Task<(string stdout, string stderr)> GetLogsAsync(HostedApp app, int lines = 200)
    {
        if (Sim)
        {
            await _runner.LogExternalAsync($"pm2 logs {app.Pm2Id} --lines {lines} --nostream", "logs tailed", true, ServiceName);
            return (SampleOut(app, lines), SampleErr(app, Math.Min(lines / 8, 12)));
        }
        var outp = await _runner.RunAsync($"pm2 logs {app.Pm2Id} --lines {lines} --nostream --out", ServiceName);
        var errp = await _runner.RunAsync($"pm2 logs {app.Pm2Id} --lines {lines} --nostream --err", ServiceName);
        return (outp.Output, errp.Output);
    }

    public async Task<ProcResult> FlushLogsAsync(int pm2Id)
    {
        var result = await _runner.RunAsync($"pm2 flush {pm2Id}", ServiceName);
        return new ProcResult(result.Success, result.Output, pm2Id);
    }

    public async Task<ProcResult> SavePm2ListAsync()
    {
        var result = await _runner.RunAsync("pm2 save", ServiceName);
        return new ProcResult(result.Success, result.Output);
    }

    public async Task<string> GetAllAppsAsync()
    {
        var result = await _runner.RunAsync("pm2 list", ServiceName);
        return result.Output;
    }

    public string GenerateEcosystem(HostedApp app, IEnumerable<HostedAppEnv> env)
    {
        var envObj = new StringBuilder("{ PORT: ").Append(app.Port).Append(", NODE_ENV: \"production\"");
        foreach (var e in env)
            envObj.Append(", ").Append(JsonSerializer.Serialize(e.Key)).Append(": ").Append(JsonSerializer.Serialize(e.Value));
        envObj.Append(" }");

        return $$"""
module.exports = {
  apps: [{
    name: "app-{{app.Id}}",
    script: "{{app.EntryPoint}}",
    cwd: "{{app.AppPath}}",
    instances: {{app.ProcessCount}},
    exec_mode: "{{(app.ClusterMode || app.ProcessCount > 1 ? "cluster" : "fork")}}",
    watch: {{(app.WatchMode ? "true" : "false")}},
    env: {{envObj}},
    error_file: "/var/log/srxpanel/apps/{{app.Id}}/error.log",
    out_file: "/var/log/srxpanel/apps/{{app.Id}}/out.log",
    max_memory_restart: "{{app.MaxMemoryRestartMB}}M"
  }]
};
""";
    }

    // ---------------- simulation ----------------

    public static ProcMetrics SimulatedMetrics(HostedApp app)
    {
        if (app.Status == HostedAppStatus.Stopped)
            return new ProcMetrics("stopped", 0, 0, 0, 0, app.RestartCount);
        var rnd = new Random();
        return new ProcMetrics("online",
            app.StartedAt.HasValue ? (long)(DateTime.UtcNow - app.StartedAt.Value).TotalSeconds : rnd.Next(60, 90000),
            Math.Round(1 + rnd.NextDouble() * 7, 1),
            Math.Round(50 + rnd.NextDouble() * 150, 1),
            app.Pid ?? rnd.Next(1000, 32000),
            app.RestartCount);
    }

    private static string SampleOut(HostedApp app, int lines)
    {
        var now = DateTime.UtcNow;
        var rnd = new Random(app.Id);
        var sb = new StringBuilder();
        sb.AppendLine($"{now.AddSeconds(-lines):HH:mm:ss} Server running on port {app.Port}");
        string[] routes = { "GET / 200", "GET /api/health 200", "POST /api/orders 201", "GET /assets/app.js 304", "GET /api/users 200" };
        for (var i = lines - 1; i > 0; i--)
            sb.AppendLine($"{now.AddSeconds(-i):HH:mm:ss} {routes[rnd.Next(routes.Length)]} {rnd.Next(3, 90)}ms");
        return sb.ToString();
    }

    private static string SampleErr(HostedApp app, int lines)
    {
        var now = DateTime.UtcNow;
        var sb = new StringBuilder();
        for (var i = lines; i > 0; i--)
            sb.AppendLine($"{now.AddSeconds(-i * 30):HH:mm:ss} [warn] deprecation notice in dependency (safe to ignore)");
        return sb.ToString();
    }

    private static ProcMetrics ParseMetrics(string json, HostedApp app)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var proc in doc.RootElement.EnumerateArray())
            {
                if (proc.TryGetProperty("pm_id", out var id) && id.GetInt32() == app.Pm2Id)
                {
                    var monit = proc.GetProperty("monit");
                    var pm2env = proc.GetProperty("pm2_env");
                    return new ProcMetrics(
                        pm2env.GetProperty("status").GetString() ?? "unknown",
                        pm2env.TryGetProperty("pm_uptime", out var up) ? up.GetInt64() : 0,
                        monit.TryGetProperty("cpu", out var cpu) ? cpu.GetDouble() : 0,
                        monit.TryGetProperty("memory", out var mem) ? mem.GetDouble() / 1024 / 1024 : 0,
                        proc.TryGetProperty("pid", out var pid) ? pid.GetInt32() : 0,
                        pm2env.TryGetProperty("restart_time", out var rt) ? rt.GetInt32() : 0);
                }
            }
        }
        catch { /* fall through */ }
        return new ProcMetrics("unknown", 0, 0, 0, 0, app.RestartCount);
    }
}
