using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Reseller;

namespace SRXPanel.Services.Api;

public record CreateAccountReq(string Username, string Password, string Email, string Plan, string? Domain);
public record UsernameReq(string Username);
public record SuspendReq(string Username, string? Reason);
public record ChangePasswordReq(string Username, string NewPassword);
public record ChangePackageReq(string Username, string NewPlan);
public record CreateDomainReq(string DomainName);
public record CreateEmailReq(string LocalPart, int DomainId);
public record CreateDatabaseReq(string Suffix);

public static class ApiEndpoints
{
    // ---------- Referral tracking ----------
    public static void MapReferralTracking(this WebApplication app)
    {
        app.MapGet("/ref/{code}", async (string code, HttpContext ctx, IAffiliateService affiliates) =>
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            var utm = ctx.Request.Query["utm_source"].FirstOrDefault();
            await affiliates.RecordClickAsync(code, ip, utm);

            ctx.Response.Cookies.Append("srx_ref", code, new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddDays(30),
                HttpOnly = false,
                IsEssential = true
            });
            return Results.Redirect("/Pricing");
        }).AllowAnonymous();
    }

    // ---------- WHMCS + Blesta provisioning APIs ----------
    public static void MapProvisioningApis(this WebApplication app)
    {
        foreach (var integration in new[] { "whmcs", "blesta" })
        {
            var group = app.MapGroup($"/api/v1/{integration}").AllowAnonymous();

            group.MapPost("/CreateAccount", async (CreateAccountReq req, HttpContext ctx, IApiAuthService auth, IApiAccountService accounts) =>
                await Guarded(ctx, auth, integration, async () =>
                {
                    var (ok, msg, data) = await accounts.CreateAccountAsync(req.Username, req.Password, req.Email, req.Plan, req.Domain);
                    return Reply(integration, ok, msg, data);
                }));

            group.MapPost("/SuspendAccount", async (SuspendReq req, HttpContext ctx, IApiAuthService auth, IApiAccountService accounts) =>
                await Guarded(ctx, auth, integration, async () =>
                {
                    var (ok, msg) = await accounts.SuspendAsync(req.Username, req.Reason);
                    return Reply(integration, ok, msg, null);
                }));

            group.MapPost("/UnsuspendAccount", async (UsernameReq req, HttpContext ctx, IApiAuthService auth, IApiAccountService accounts) =>
                await Guarded(ctx, auth, integration, async () =>
                {
                    var (ok, msg) = await accounts.UnsuspendAsync(req.Username);
                    return Reply(integration, ok, msg, null);
                }));

            group.MapPost("/TerminateAccount", async (UsernameReq req, HttpContext ctx, IApiAuthService auth, IApiAccountService accounts) =>
                await Guarded(ctx, auth, integration, async () =>
                {
                    var (ok, msg) = await accounts.TerminateAsync(req.Username);
                    return Reply(integration, ok, msg, null);
                }));

            group.MapPost("/ChangePassword", async (ChangePasswordReq req, HttpContext ctx, IApiAuthService auth, IApiAccountService accounts) =>
                await Guarded(ctx, auth, integration, async () =>
                {
                    var (ok, msg) = await accounts.ChangePasswordAsync(req.Username, req.NewPassword);
                    return Reply(integration, ok, msg, null);
                }));

            group.MapPost("/ChangePackage", async (ChangePackageReq req, HttpContext ctx, IApiAuthService auth, IApiAccountService accounts) =>
                await Guarded(ctx, auth, integration, async () =>
                {
                    var (ok, msg) = await accounts.ChangePackageAsync(req.Username, req.NewPlan);
                    return Reply(integration, ok, msg, null);
                }));

            group.MapGet("/GetUsage", async (string username, HttpContext ctx, IApiAuthService auth, IApiAccountService accounts) =>
                await Guarded(ctx, auth, integration, async () =>
                {
                    var (ok, data) = await accounts.GetUsageAsync(username);
                    return Reply(integration, ok, ok ? "ok" : "Account not found", data);
                }));
        }
    }

    // ---------- Health check ----------
    private static readonly DateTime StartedAtUtc = DateTime.UtcNow;

    public static void MapHealthCheck(this WebApplication app)
    {
        app.MapGet("/api/health", async (ApplicationDbContext db, ISystemStatsService stats) =>
        {
            // Database — a trivial query proves connectivity + migrations applied.
            var dbOk = false;
            try { dbOk = await db.Database.CanConnectAsync(); }
            catch { dbOk = false; }

            var system = await stats.GetStatsAsync();
            var uptime = DateTime.UtcNow - StartedAtUtc;

            var overall = dbOk ? "healthy" : "degraded";

            return Results.Json(new
            {
                status = overall,
                version = AppInfo.Version,
                services = new
                {
                    database = dbOk ? "ok" : "error",
                    nginx = ServiceState("nginx"),
                    mysql = ServiceState("mysql"),
                    email = ServiceState("dovecot"),
                    dns = ServiceState("bind9")
                },
                uptime = FormatUptime(uptime),
                diskUsage = $"{system.DiskUsagePercent:0}%",
                memoryUsage = $"{system.MemoryUsagePercent:0}%"
            }, statusCode: dbOk ? 200 : 503);
        }).AllowAnonymous();
    }

    /// <summary>
    /// Reports whether a systemd unit is active. On non-Linux/dev hosts there is no
    /// systemctl, so we report "simulated" rather than a misleading "stopped".
    /// </summary>
    private static string ServiceState(string unit)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "simulated";

        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "systemctl",
                Arguments = $"is-active {unit}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (proc == null) return "unknown";
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(2000);
            return output == "active" ? "running" : (string.IsNullOrEmpty(output) ? "stopped" : output);
        }
        catch
        {
            return "unknown";
        }
    }

    private static string FormatUptime(TimeSpan t) =>
        $"{(int)t.TotalDays}d {t.Hours}h {t.Minutes}m";

    // ---------- REST API (JWT) ----------
    public static void MapRestApi(this WebApplication app)
    {
        // Exchange an API key for a JWT.
        app.MapPost("/api/v1/auth/token", async (HttpContext ctx, IApiAuthService auth, IJwtTokenService jwt) =>
        {
            var raw = ctx.Request.Headers["X-API-Key"].FirstOrDefault();
            var (user, key) = await auth.AuthenticateAsync(raw);
            if (user == null)
            {
                await auth.LogAsync(null, "POST", "/api/v1/auth/token", "rest", 401, Ip(ctx), "unauthorized");
                return Results.Json(new { error = "Invalid API key" }, statusCode: 401);
            }
            await auth.LogAsync(key, "POST", "/api/v1/auth/token", "rest", 200, Ip(ctx), "token issued");
            return Results.Json(new { access_token = jwt.CreateToken(user), token_type = "Bearer", expires_in = 86400 });
        }).AllowAnonymous();

        var api = app.MapGroup("/api/v1")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "Bearer" });

        api.MapGet("/account", async (HttpContext ctx, ApplicationDbContext db) =>
        {
            var user = await CurrentUser(ctx, db);
            if (user == null) return Results.Unauthorized();
            return Results.Json(new { user.Id, user.UserName, user.Email, user.FullName, user.DiskQuotaMB, user.BandwidthQuotaMB, user.IsActive });
        });

        api.MapGet("/domains", async (HttpContext ctx, ApplicationDbContext db) =>
        {
            var uid = Uid(ctx);
            var domains = await db.Domains.Where(d => d.UserId == uid)
                .Select(d => new { d.Id, d.DomainName, d.PhpVersion, d.IsActive }).ToListAsync();
            return Results.Json(domains);
        });

        api.MapPost("/domains", async (CreateDomainReq req, HttpContext ctx, ApplicationDbContext db, IWebhookDispatcher hooks) =>
        {
            var uid = Uid(ctx)!;
            var name = req.DomainName.Trim().ToLowerInvariant();
            if (await db.Domains.AnyAsync(d => d.DomainName == name))
                return Results.Json(new { error = "Domain already exists" }, statusCode: 409);
            var domain = new Domain
            {
                UserId = uid, DomainName = name,
                DocumentRoot = $"/home/{HostingHelpers.UserPrefix(ctx.User.FindFirstValue("username") ?? "user")}/public_html/{name}",
                PhpVersion = "8.3", IsActive = true, CreatedAt = DateTime.UtcNow
            };
            db.Domains.Add(domain);
            await db.SaveChangesAsync();
            await hooks.DispatchAsync(uid, "domain.created", new { domain.Id, domain.DomainName });
            return Results.Json(new { domain.Id, domain.DomainName }, statusCode: 201);
        });

        api.MapDelete("/domains/{id:int}", async (int id, HttpContext ctx, ApplicationDbContext db, IWebhookDispatcher hooks) =>
        {
            var uid = Uid(ctx)!;
            var domain = await db.Domains.FirstOrDefaultAsync(d => d.Id == id && d.UserId == uid);
            if (domain == null) return Results.NotFound();
            db.Domains.Remove(domain);
            await db.SaveChangesAsync();
            await hooks.DispatchAsync(uid, "domain.deleted", new { id, domain.DomainName });
            return Results.NoContent();
        });

        api.MapGet("/emails", async (HttpContext ctx, ApplicationDbContext db) =>
        {
            var uid = Uid(ctx);
            var emails = await db.EmailAccounts.Where(e => e.UserId == uid)
                .Select(e => new { e.Id, e.EmailAddress, e.CreatedAt }).ToListAsync();
            return Results.Json(emails);
        });

        api.MapPost("/emails", async (CreateEmailReq req, HttpContext ctx, ApplicationDbContext db, IWebhookDispatcher hooks) =>
        {
            var uid = Uid(ctx)!;
            var domain = await db.Domains.FirstOrDefaultAsync(d => d.Id == req.DomainId && d.UserId == uid);
            if (domain == null) return Results.Json(new { error = "Domain not found" }, statusCode: 404);
            var address = $"{req.LocalPart.ToLowerInvariant()}@{domain.DomainName}";
            if (await db.EmailAccounts.AnyAsync(e => e.EmailAddress == address))
                return Results.Json(new { error = "Email already exists" }, statusCode: 409);
            var email = new EmailAccount { UserId = uid, DomainId = domain.Id, EmailAddress = address, PasswordHash = "", CreatedAt = DateTime.UtcNow };
            db.EmailAccounts.Add(email);
            await db.SaveChangesAsync();
            await hooks.DispatchAsync(uid, "email.created", new { email.Id, email.EmailAddress });
            return Results.Json(new { email.Id, email.EmailAddress }, statusCode: 201);
        });

        api.MapGet("/databases", async (HttpContext ctx, ApplicationDbContext db) =>
        {
            var uid = Uid(ctx);
            var dbs = await db.Databases.Where(d => d.UserId == uid)
                .Select(d => new { d.Id, d.DbName, d.CreatedAt }).ToListAsync();
            return Results.Json(dbs);
        });

        api.MapPost("/databases", async (CreateDatabaseReq req, HttpContext ctx, ApplicationDbContext db) =>
        {
            var uid = Uid(ctx)!;
            var name = HostingHelpers.Prefixed(ctx.User.FindFirstValue("username") ?? "user", req.Suffix);
            if (await db.Databases.AnyAsync(d => d.DbName == name))
                return Results.Json(new { error = "Database already exists" }, statusCode: 409);
            var database = new Database { UserId = uid, DbName = name, DbUser = name, DbPasswordHash = "", CreatedAt = DateTime.UtcNow };
            db.Databases.Add(database);
            await db.SaveChangesAsync();
            return Results.Json(new { database.Id, database.DbName }, statusCode: 201);
        });

        api.MapGet("/stats/usage", async (HttpContext ctx, ApplicationDbContext db, IFileManagerService files) =>
        {
            var uid = Uid(ctx)!;
            return Results.Json(new
            {
                disk_used_mb = files.GetUsedBytes(uid) / 1024 / 1024,
                domains = await db.Domains.CountAsync(d => d.UserId == uid),
                emails = await db.EmailAccounts.CountAsync(e => e.UserId == uid),
                databases = await db.Databases.CountAsync(d => d.UserId == uid)
            });
        });
    }

    // ---------- helpers ----------
    private static string? Ip(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString();
    private static string? Uid(HttpContext ctx) => ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
    private static Task<ApplicationUser?> CurrentUser(HttpContext ctx, ApplicationDbContext db) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == Uid(ctx));

    private static async Task<IResult> Guarded(HttpContext ctx, IApiAuthService auth, string integration, Func<Task<IResult>> work)
    {
        var raw = ctx.Request.Headers["X-API-Key"].FirstOrDefault();
        var (user, key) = await auth.AuthenticateAsync(raw);
        var path = ctx.Request.Path.Value ?? "";
        if (user == null)
        {
            await auth.LogAsync(null, ctx.Request.Method, path, integration, 401, Ip(ctx), "unauthorized");
            return Results.Json(new { result = "error", message = "Invalid API key" }, statusCode: 401);
        }
        if (!auth.RateLimitOk(key!.Prefix))
        {
            await auth.LogAsync(key, ctx.Request.Method, path, integration, 429, Ip(ctx), "rate limited");
            return Results.Json(new { result = "error", message = "Rate limit exceeded" }, statusCode: 429);
        }
        var result = await work();
        await auth.LogAsync(key, ctx.Request.Method, path, integration, 200, Ip(ctx), "ok");
        return result;
    }

    private static IResult Reply(string integration, bool ok, string message, object? data)
    {
        // WHMCS expects result: success/error; Blesta expects success: bool.
        if (integration == "blesta")
            return Results.Json(new { success = ok, message, data });
        return Results.Json(new { result = ok ? "success" : "error", message, data });
    }
}
