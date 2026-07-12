using System.Text;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.AppHosting;

public enum PythonFramework
{
    Flask,
    Django,
    FastAPI,
    Unknown
}

public record FrameworkDetection(PythonFramework Framework, string SuggestedCommand, string SuggestedEntry);

/// <summary>
/// Runs Python apps under Gunicorn (WSGI) or Uvicorn (ASGI). Simulation-safe like the PM2 service.
/// Detects the framework from requirements.txt to suggest the right start command.
/// </summary>
public interface IGunicornService
{
    Task<ProcResult> StartAppAsync(HostedApp app);
    Task<ProcResult> StopAppAsync(HostedApp app);
    Task<ProcResult> RestartAppAsync(HostedApp app);
    Task<ProcMetrics> GetStatusAsync(HostedApp app);
    Task<(string stdout, string stderr)> GetLogsAsync(HostedApp app, int lines = 200);
    Task<FrameworkDetection> DetectFrameworkAsync(string appPath);
}

public class GunicornService : IGunicornService
{
    private const string ServiceName = "gunicorn";

    private readonly ICommandRunner _runner;

    public GunicornService(ICommandRunner runner) => _runner = runner;

    private bool Sim => _runner.SimulationMode;

    public async Task<ProcResult> StartAppAsync(HostedApp app)
    {
        var cmd = string.IsNullOrWhiteSpace(app.StartCommand)
            ? $"gunicorn --workers {app.ProcessCount} --bind 127.0.0.1:{app.Port} {app.EntryPoint}"
            : app.StartCommand;

        if (Sim)
        {
            await _runner.LogExternalAsync($"systemd start app-{app.Id} ({app.Name})",
                $"Would run: {cmd} (venv {app.AppPath}/.venv)", true, ServiceName);
            return new ProcResult(true, $"[gunicorn] {app.Name} started on 127.0.0.1:{app.Port}", app.Id);
        }

        var full = $"cd {app.AppPath} && source .venv/bin/activate && {cmd} --daemon --pid /tmp/app-{app.Id}.pid";
        var result = await _runner.RunAsync(full, ServiceName);
        return new ProcResult(result.Success, result.Output, app.Id);
    }

    public async Task<ProcResult> StopAppAsync(HostedApp app)
    {
        var result = await _runner.RunAsync($"kill $(cat /tmp/app-{app.Id}.pid 2>/dev/null) 2>/dev/null || true", ServiceName);
        return new ProcResult(true, result.Output, app.Id);
    }

    public async Task<ProcResult> RestartAppAsync(HostedApp app)
    {
        await StopAppAsync(app);
        return await StartAppAsync(app);
    }

    public async Task<ProcMetrics> GetStatusAsync(HostedApp app)
    {
        if (Sim)
        {
            await _runner.LogExternalAsync($"ps -p app-{app.Id}", "status probed", true, ServiceName);
            return Pm2Service.SimulatedMetrics(app);
        }
        var result = await _runner.RunAsync($"ps -o %cpu,%mem,etimes -p $(cat /tmp/app-{app.Id}.pid 2>/dev/null) --no-headers", ServiceName);
        var parts = result.Output.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3 && double.TryParse(parts[0], out var cpu) && double.TryParse(parts[1], out var mem) && long.TryParse(parts[2], out var up))
            return new ProcMetrics("online", up, cpu, mem, app.Pid ?? 0, app.RestartCount);
        return new ProcMetrics(app.Status == HostedAppStatus.Running ? "online" : "stopped", 0, 0, 0, 0, app.RestartCount);
    }

    public async Task<(string stdout, string stderr)> GetLogsAsync(HostedApp app, int lines = 200)
    {
        if (Sim)
        {
            await _runner.LogExternalAsync($"journalctl -u app-{app.Id} -n {lines}", "logs tailed", true, ServiceName);
            var now = DateTime.UtcNow;
            var rnd = new Random(app.Id);
            var sb = new StringBuilder();
            sb.AppendLine($"[{now.AddSeconds(-lines):HH:mm:ss}] [INFO] Started server process on 127.0.0.1:{app.Port}");
            sb.AppendLine($"[{now.AddSeconds(-lines):HH:mm:ss}] [INFO] Application startup complete");
            string[] lineTpl = { "GET / HTTP/1.1\" 200", "GET /health HTTP/1.1\" 200", "POST /api/v1/items HTTP/1.1\" 201", "GET /docs HTTP/1.1\" 200" };
            for (var i = lines - 2; i > 0; i--)
                sb.AppendLine($"[{now.AddSeconds(-i):HH:mm:ss}] [INFO] 127.0.0.1 - \"{lineTpl[rnd.Next(lineTpl.Length)]} - {rnd.Next(2, 60)}ms");
            var err = new StringBuilder();
            err.AppendLine($"[{now.AddMinutes(-5):HH:mm:ss}] [WARNING] Worker reloading due to code change");
            return (sb.ToString(), err.ToString());
        }
        var result = await _runner.RunAsync($"journalctl -u app-{app.Id} -n {lines} --no-pager", ServiceName);
        return (result.Output, "");
    }

    public async Task<FrameworkDetection> DetectFrameworkAsync(string appPath)
    {
        string requirements = "";
        var reqFile = Path.Combine(appPath ?? "", "requirements.txt");
        try { if (!Sim && File.Exists(reqFile)) requirements = await File.ReadAllTextAsync(reqFile); }
        catch { /* ignore */ }

        await _runner.LogExternalAsync($"cat {appPath}/requirements.txt", "requirements read", Sim, ServiceName);

        var lower = requirements.ToLowerInvariant();
        if (lower.Contains("django"))
            return new FrameworkDetection(PythonFramework.Django, "gunicorn project.wsgi:application", "project/wsgi.py");
        if (lower.Contains("fastapi") || lower.Contains("uvicorn"))
            return new FrameworkDetection(PythonFramework.FastAPI, "uvicorn main:app --host 127.0.0.1", "main.py");
        if (lower.Contains("flask"))
            return new FrameworkDetection(PythonFramework.Flask, "gunicorn app:app", "app.py");

        // Simulation / unknown: suggest FastAPI as the modern default.
        return new FrameworkDetection(PythonFramework.Unknown, "gunicorn app:app", "app.py");
    }
}
