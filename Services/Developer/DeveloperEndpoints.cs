using System.Net.WebSockets;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Developer;

public static class DeveloperEndpoints
{
    /// <summary>
    /// Git provider webhook: POST /api/git/webhook/{repoId}/{secret}.
    /// The secret is compared in constant time inside the service. Auto-deploy must be on.
    /// </summary>
    public static void MapGitWebhook(this WebApplication app)
    {
        app.MapPost("/api/git/webhook/{repoId:int}/{secret}", async (
            int repoId, string secret, HttpRequest request, IGitDeployService git) =>
        {
            var repo = await git.FindByWebhookAsync(repoId, secret);
            if (repo == null) return Results.Unauthorized();

            if (!repo.AutoDeploy)
                return Results.Ok(new { received = true, deployed = false, reason = "auto-deploy is disabled" });

            // GitHub, GitLab and Bitbucket all name the pushed ref differently; if we can read
            // one, only deploy when it matches the configured branch.
            var pushedBranch = await ReadBranchAsync(request);
            if (pushedBranch != null && !pushedBranch.Equals(repo.Branch, StringComparison.OrdinalIgnoreCase))
                return Results.Ok(new { received = true, deployed = false, reason = $"push was to '{pushedBranch}', not '{repo.Branch}'" });

            try
            {
                var deploymentId = await git.DeployAsync(repo.Id, GitTriggerType.Webhook);
                return Results.Ok(new { received = true, deployed = true, deploymentId });
            }
            catch (InvalidOperationException ex)
            {
                // Providers retry on non-2xx, and pushes arrive in bursts. Acknowledge the
                // delivery rather than letting it surface as a 500 and trigger another retry.
                return Results.Ok(new { received = true, deployed = false, reason = ex.Message });
            }
        }).AllowAnonymous();
    }

    private static async Task<string?> ReadBranchAsync(HttpRequest request)
    {
        try
        {
            request.EnableBuffering();
            using var reader = new StreamReader(request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            request.Body.Position = 0;

            if (string.IsNullOrWhiteSpace(body)) return null;

            using var document = System.Text.Json.JsonDocument.Parse(body);
            var root = document.RootElement;

            // GitHub / GitLab: {"ref": "refs/heads/main"}
            if (root.TryGetProperty("ref", out var refElement) && refElement.GetString() is string reference)
                return reference.Replace("refs/heads/", "");

            // Bitbucket: {"push": {"changes": [{"new": {"name": "main"}}]}}
            if (root.TryGetProperty("push", out var push) &&
                push.TryGetProperty("changes", out var changes) &&
                changes.GetArrayLength() > 0 &&
                changes[0].TryGetProperty("new", out var newRef) &&
                newRef.TryGetProperty("name", out var name))
                return name.GetString();

            return null;
        }
        catch (Exception)
        {
            // A payload we cannot parse simply means "deploy the configured branch".
            return null;
        }
    }

    /// <summary>
    /// Browser terminal: GET /ws/terminal/{token} (WebSocket upgrade).
    /// The token is a five-minute JWT minted by the Terminal page for the signed-in user.
    /// </summary>
    public static void MapTerminalWebSocket(this WebApplication app)
    {
        app.Map("/ws/terminal/{token}", async (HttpContext context, string token,
            ITerminalService terminals, IServiceScopeFactory scopeFactory,
            ILogger<DevToolsHub> logger) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("This endpoint expects a WebSocket upgrade.");
                return;
            }

            var (ok, userId, tokenId, error) = terminals.ValidateTicket(token);
            if (!ok || userId == null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync(error ?? "Invalid token.");
                return;
            }

            if (await terminals.CountActiveAsync(userId) >= ITerminalService.MaxConcurrentSessions)
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.Response.WriteAsync(
                    $"You already have {ITerminalService.MaxConcurrentSessions} terminal sessions open.");
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var userAgent = context.Request.Headers.UserAgent.ToString();

            var sessionId = await terminals.OpenSessionAsync(userId, tokenId ?? "", ip, userAgent);
            logger.LogInformation("Terminal session {SessionId} opened for {UserId} from {Ip}", sessionId, userId, ip);

            try
            {
                await RunTerminalLoopAsync(socket, scopeFactory, userId, sessionId, logger);
            }
            catch (WebSocketException)
            {
                // The browser went away — nothing to report.
            }
            finally
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ITerminalService>().CloseSessionAsync(sessionId);
                logger.LogInformation("Terminal session {SessionId} closed", sessionId);
            }
        });
    }

    private static async Task RunTerminalLoopAsync(WebSocket socket, IServiceScopeFactory scopeFactory,
        string userId, int sessionId, ILogger logger)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<ApplicationDbContext>();
        var files = sp.GetRequiredService<IFileManagerService>();
        var runner = sp.GetRequiredService<ICommandRunner>();
        var terminals = sp.GetRequiredService<ITerminalService>();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        var shell = new SandboxedShell(files, runner, userId, user?.UserName ?? "user");

        await SendAsync(socket, shell.Banner);
        await SendAsync(socket, shell.Prompt);

        var buffer = new byte[4096];
        var line = new StringBuilder();
        var commandCount = 0;

        // The idle timeout doubles as the receive timeout: no input for 30 minutes ends the session.
        using var idle = new CancellationTokenSource(TerminalService.IdleTimeout);

        while (socket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult received;
            try
            {
                received = await socket.ReceiveAsync(buffer, idle.Token);
            }
            catch (OperationCanceledException)
            {
                await SendAsync(socket, "\r\n\u001b[33mSession timed out after 30 minutes of inactivity.\u001b[0m\r\n");
                await CloseAsync(socket, "idle timeout");
                return;
            }

            if (received.MessageType == WebSocketMessageType.Close)
            {
                await CloseAsync(socket, "client closed");
                return;
            }

            idle.CancelAfter(TerminalService.IdleTimeout);

            // An admin can kill the session from /Admin/Developer/Terminals.
            if (await terminals.IsTerminationRequestedAsync(sessionId))
            {
                await SendAsync(socket, "\r\n\u001b[31mThis session was terminated by an administrator.\u001b[0m\r\n");
                await CloseAsync(socket, "terminated by admin");
                return;
            }

            var text = Encoding.UTF8.GetString(buffer, 0, received.Count);

            foreach (var c in text)
            {
                switch (c)
                {
                    case '\r':
                    case '\n':
                    {
                        await SendAsync(socket, "\r\n");
                        var command = line.ToString();
                        line.Clear();

                        if (command.Trim() is "exit" or "logout")
                        {
                            await SendAsync(socket, "logout\r\n");
                            await CloseAsync(socket, "user exited");
                            return;
                        }

                        if (command.Trim().Length > 0)
                        {
                            commandCount++;
                            string output;
                            try
                            {
                                output = await shell.ExecuteAsync(command);
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning(ex, "Terminal command failed in session {SessionId}", sessionId);
                                output = $"\u001b[31m{ex.Message}\u001b[0m\r\n";
                            }
                            if (output.Length > 0) await SendAsync(socket, output);
                            await terminals.TouchAsync(sessionId, commandCount);
                        }

                        await SendAsync(socket, shell.Prompt);
                        break;
                    }

                    // Backspace / delete.
                    case '\u007f':
                    case '\b':
                        if (line.Length > 0)
                        {
                            line.Length--;
                            await SendAsync(socket, "\b \b");
                        }
                        break;

                    // Ctrl+C.
                    case '\u0003':
                        line.Clear();
                        await SendAsync(socket, "^C\r\n");
                        await SendAsync(socket, shell.Prompt);
                        break;

                    // Ctrl+D on an empty line.
                    case '\u0004':
                        if (line.Length == 0)
                        {
                            await SendAsync(socket, "logout\r\n");
                            await CloseAsync(socket, "eof");
                            return;
                        }
                        break;

                    default:
                        if (!char.IsControl(c))
                        {
                            line.Append(c);
                            await SendAsync(socket, c.ToString()); // local echo
                        }
                        break;
                }
            }
        }
    }

    private static Task SendAsync(WebSocket socket, string text) =>
        socket.State == WebSocketState.Open
            ? socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None)
            : Task.CompletedTask;

    private static async Task CloseAsync(WebSocket socket, string reason)
    {
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, CancellationToken.None);
    }
}

/// <summary>
/// Housekeeping for the developer tools: expires idle terminal sessions and deletes
/// staging sites that have reached their expiry date.
/// </summary>
public class DeveloperMaintenanceService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeveloperMaintenanceService> _logger;

    public DeveloperMaintenanceService(IServiceScopeFactory scopeFactory, ILogger<DeveloperMaintenanceService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();

                var terminals = scope.ServiceProvider.GetRequiredService<ITerminalService>();
                var reaped = await terminals.ReapIdleSessionsAsync(TerminalService.IdleTimeout);
                if (reaped > 0) _logger.LogInformation("Closed {Count} idle terminal sessions", reaped);

                var staging = scope.ServiceProvider.GetRequiredService<IStagingService>();
                var expired = await staging.ReapExpiredAsync();
                if (expired > 0) _logger.LogInformation("Deleted {Count} expired staging sites", expired);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Developer maintenance tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
