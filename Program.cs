using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using DXPanel.Data;
using DXPanel.Models;
using DXPanel.Services;
using DXPanel.Services.Api;
using DXPanel.Services.Developer;
using DXPanel.Services.Public;
using DXPanel.Services.Vps;

var builder = WebApplication.CreateBuilder(args);

// Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=dxpanel.db";
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

// Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // Password policy: min 8 chars, uppercase, number, special char
    options.Password.RequiredLength = 8;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireDigit = true;
    options.Password.RequireNonAlphanumeric = true;

    options.User.RequireUniqueEmail = true;

    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();

// Persist DataProtection keys to the data directory so login cookies, antiforgery
// tokens, 2FA and password-reset tokens survive restarts. Critical in containers,
// where the filesystem is ephemeral — without this every restart regenerates the
// keys and logs everyone out. Defaults next to the SQLite DB (the mounted volume).
var keysDirectory = builder.Configuration["DataProtection:KeysDirectory"];
if (string.IsNullOrWhiteSpace(keysDirectory))
    keysDirectory = Path.Combine(builder.Environment.ContentRootPath, "data", "keys");
try
{
    Directory.CreateDirectory(keysDirectory);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory))
        .SetApplicationName("DXPanel");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"DataProtection: could not persist keys to '{keysDirectory}': {ex.Message}. Using default key storage.");
}

// When running behind a reverse proxy (the Docker nginx), honor X-Forwarded-* so
// the app sees the original scheme/client IP, and let the proxy own TLS/redirects.
var behindProxy = builder.Configuration.GetValue<bool>("ForwardedHeaders:Enabled");
if (behindProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // The panel container is only reachable from our own proxy on a private
        // network, so trust the forwarded headers it sends.
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });
}
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<ISystemStatsService, SystemStatsService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IUserScopeService, UserScopeService>();
builder.Services.AddSingleton<ISecretHasher, BCryptSecretHasher>();
builder.Services.AddSingleton<IRateLimitService, RateLimitService>();
builder.Services.AddSingleton<IFileManagerService, FileManagerService>();

// Phase 3 — Linux service integration (simulation-aware)
builder.Services.Configure<DXPanel.Services.PanelSettings>(builder.Configuration.GetSection("Panel"));
builder.Services.AddScoped<DXPanel.Services.ISettingsWriter, DXPanel.Services.SettingsWriter>();
builder.Services.AddScoped<DXPanel.Services.Interfaces.ICommandRunner, DXPanel.Services.Integration.CommandRunner>();
builder.Services.AddScoped<DXPanel.Services.Interfaces.INginxService, DXPanel.Services.Integration.NginxService>();
builder.Services.AddScoped<DXPanel.Services.Interfaces.IMySqlService, DXPanel.Services.Integration.MySqlService>();
builder.Services.AddScoped<DXPanel.Services.Interfaces.IDnsService, DXPanel.Services.Integration.DnsService>();
builder.Services.AddScoped<DXPanel.Services.Interfaces.IFtpService, DXPanel.Services.Integration.FtpService>();
builder.Services.AddScoped<DXPanel.Services.Interfaces.IEmailService, DXPanel.Services.Integration.EmailService>();
builder.Services.AddScoped<DXPanel.Services.Interfaces.ISslService, DXPanel.Services.Integration.SslService>();

// SMS (Twilio) — real send in production, logged in simulation.
builder.Services.Configure<DXPanel.Services.TwilioSettings>(builder.Configuration.GetSection("Twilio"));
builder.Services.AddScoped<DXPanel.Services.Integration.ITwilioService, DXPanel.Services.Integration.TwilioService>();

// Off-site backup (S3 / Backblaze B2) — real upload in production, logged in simulation.
builder.Services.Configure<DXPanel.Services.BackupSettings>(builder.Configuration.GetSection("Backup"));
builder.Services.AddScoped<DXPanel.Services.Integration.IOffSiteBackupService, DXPanel.Services.Integration.OffSiteBackupService>();

// Exchange rates — daily refresh from a free API, cached in the ExchangeRate table.
builder.Services.AddHttpClient("exchange", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("DXPanel-ExchangeRates/1.0");
});
builder.Services.AddScoped<DXPanel.Services.Integration.IExchangeRateService, DXPanel.Services.Integration.ExchangeRateService>();
builder.Services.AddHostedService<DXPanel.Services.Integration.RefreshExchangeRatesJob>();

// Phase 4 — Payments, provisioning, billing
builder.Services.Configure<DXPanel.Services.StripeSettings>(builder.Configuration.GetSection("Stripe"));
builder.Services.AddScoped<DXPanel.Services.Billing.IStripeGateway, DXPanel.Services.Billing.StripeGateway>();
builder.Services.AddScoped<DXPanel.Services.Billing.IMailerService, DXPanel.Services.Billing.MailerService>();
builder.Services.AddScoped<DXPanel.Services.Billing.IProvisioningService, DXPanel.Services.Billing.ProvisioningService>();
builder.Services.AddScoped<DXPanel.Services.Billing.IBillingService, DXPanel.Services.Billing.BillingService>();
builder.Services.AddHostedService<DXPanel.Services.Billing.TrialDunningBackgroundService>();

// Phase 5 — Customer self-service portal
builder.Services.AddScoped<DXPanel.Services.Portal.ITicketService, DXPanel.Services.Portal.TicketService>();
builder.Services.AddScoped<DXPanel.Services.Portal.IBackupService, DXPanel.Services.Portal.BackupService>();
builder.Services.AddScoped<DXPanel.Services.Portal.IApiKeyService, DXPanel.Services.Portal.ApiKeyService>();

// Phase 6A — reseller system + white-label
builder.Services.AddScoped<DXPanel.Services.Reseller.IResellerService, DXPanel.Services.Reseller.ResellerService>();
builder.Services.AddScoped<DXPanel.Services.Reseller.IResourceGuard, DXPanel.Services.Reseller.ResourceGuard>();
builder.Services.AddScoped<DXPanel.Services.Reseller.IBrandingResolver, DXPanel.Services.Reseller.BrandingResolver>();

// Phase 6B — reseller billing + integrations
builder.Services.AddScoped<DXPanel.Services.IPlatformSettingsService, DXPanel.Services.PlatformSettingsService>();
builder.Services.AddScoped<DXPanel.Services.Billing.IResellerBillingService, DXPanel.Services.Billing.ResellerBillingService>();
builder.Services.AddScoped<DXPanel.Services.Reseller.ICurrencyService, DXPanel.Services.Reseller.CurrencyService>();
builder.Services.AddScoped<DXPanel.Services.Reseller.IAffiliateService, DXPanel.Services.Reseller.AffiliateService>();
builder.Services.AddScoped<DXPanel.Services.Api.IApiAuthService, DXPanel.Services.Api.ApiAuthService>();
builder.Services.AddScoped<DXPanel.Services.Api.IApiAccountService, DXPanel.Services.Api.ApiAccountService>();
builder.Services.AddScoped<DXPanel.Services.Api.IWebhookDispatcher, DXPanel.Services.Api.WebhookDispatcher>();
builder.Services.AddSingleton<DXPanel.Services.Api.IJwtTokenService, DXPanel.Services.Api.JwtTokenService>();

// Phase 7 — version management + health check
builder.Services.AddScoped<DXPanel.Services.IUpdateService, DXPanel.Services.UpdateService>();

// Phase 8 — public frontend
builder.Services.AddScoped<DXPanel.Services.IFrontendService, DXPanel.Services.FrontendService>();
builder.Services.Configure<DXPanel.Services.PublicSiteOptions>(builder.Configuration.GetSection("PanelSettings"));

// Store / upgrade system
builder.Services.AddScoped<DXPanel.Services.Store.ISmsSender, DXPanel.Services.Store.SmsSender>();
builder.Services.AddScoped<DXPanel.Services.Store.IStoreService, DXPanel.Services.Store.StoreService>();
builder.Services.AddHostedService<DXPanel.Services.Store.InvoiceReminderService>();

// Phase 9 — security package
builder.Services.AddSignalR();
builder.Services.AddSingleton<DXPanel.Services.Security.ISecurityBroadcast, DXPanel.Services.Security.SecurityBroadcast>();
builder.Services.AddScoped<DXPanel.Services.Security.IModSecurityService, DXPanel.Services.Security.ModSecurityService>();
builder.Services.AddScoped<DXPanel.Services.Security.IClamAvService, DXPanel.Services.Security.ClamAvService>();
builder.Services.AddScoped<DXPanel.Services.Security.IMalwareScanner, DXPanel.Services.Security.MalwareScannerService>();
builder.Services.AddScoped<DXPanel.Services.Security.IEmailSecurityService, DXPanel.Services.Security.EmailSecurityService>();
builder.Services.AddScoped<DXPanel.Services.Security.IBruteForceService, DXPanel.Services.Security.BruteForceProtectionService>();
builder.Services.AddScoped<DXPanel.Services.Security.IIpManagerService, DXPanel.Services.Security.IpManagerService>();
builder.Services.AddScoped<DXPanel.Services.Security.ISecurityScoreService, DXPanel.Services.Security.SecurityScoreService>();

// Phase 10 — one-click application installer
builder.Services.AddSingleton<DXPanel.Services.Apps.IInstallBroadcast, DXPanel.Services.Apps.InstallBroadcast>();
builder.Services.AddScoped<DXPanel.Services.Apps.IAppInstallerService, DXPanel.Services.Apps.AppInstallerService>();
builder.Services.AddScoped<DXPanel.Services.Apps.IWordPressManager, DXPanel.Services.Apps.WordPressManagerService>();

// Phase 11 — developer tools
builder.Services.AddSingleton<DXPanel.Services.Developer.IDevToolsBroadcast, DXPanel.Services.Developer.DevToolsBroadcast>();
builder.Services.AddScoped<DXPanel.Services.Developer.ICronService, DXPanel.Services.Developer.CronService>();
builder.Services.AddScoped<DXPanel.Services.Developer.ISshKeyService, DXPanel.Services.Developer.SshKeyService>();
builder.Services.AddScoped<DXPanel.Services.Developer.IGitDeployService, DXPanel.Services.Developer.GitDeployService>();
builder.Services.AddScoped<DXPanel.Services.Developer.ITerminalService, DXPanel.Services.Developer.TerminalService>();
builder.Services.AddScoped<DXPanel.Services.Developer.IPackageManagerService, DXPanel.Services.Developer.PackageManagerService>();
builder.Services.AddScoped<DXPanel.Services.Developer.ILogViewerService, DXPanel.Services.Developer.LogViewerService>();
builder.Services.AddScoped<DXPanel.Services.Developer.IPhpConfigService, DXPanel.Services.Developer.PhpConfigService>();
builder.Services.AddScoped<DXPanel.Services.Developer.IStagingService, DXPanel.Services.Developer.StagingService>();
builder.Services.AddScoped<DXPanel.Services.Developer.IDnsLookupService, DXPanel.Services.Developer.DnsLookupService>();
builder.Services.AddScoped<DXPanel.Services.Developer.IPerformanceService, DXPanel.Services.Developer.PerformanceService>();
builder.Services.AddScoped<DXPanel.Services.Developer.IDatabaseToolsService, DXPanel.Services.Developer.DatabaseToolsService>();
builder.Services.AddScoped<DXPanel.Services.Developer.IDeveloperSettingsService, DXPanel.Services.Developer.DeveloperSettingsService>();
builder.Services.AddHostedService<DXPanel.Services.Developer.CronBackgroundService>();
builder.Services.AddHostedService<DXPanel.Services.Developer.DeveloperMaintenanceService>();

// Outbound HTTP for the performance tester and webhook deliveries. Both talk to
// untrusted third-party hosts, so they get short timeouts and no redirect following.
builder.Services.AddHttpClient("perf", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("DXPanel-PerformanceTest/1.0");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

builder.Services.AddHttpClient("webhook", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("DXPanel-Webhook/1.0");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

// Phase 11 — OpenAPI document backing the Swagger UI at /docs/api
builder.Services.AddOpenApi();

// Phase 13 — Cloudflare integration
builder.Services.AddHttpClient("cloudflare", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("DXPanel-Cloudflare/1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
builder.Services.AddScoped<DXPanel.Services.Cloudflare.ICloudflareService, DXPanel.Services.Cloudflare.CloudflareService>();
builder.Services.AddScoped<DXPanel.Services.Cloudflare.ICloudflareManager, DXPanel.Services.Cloudflare.CloudflareManager>();
builder.Services.AddHostedService<DXPanel.Services.Cloudflare.CloudflareAnalyticsService>();

// Phase 14 — multi-server + node management
builder.Services.AddSingleton<DXPanel.Services.Nodes.INodeBroadcast, DXPanel.Services.Nodes.NodeBroadcast>();
builder.Services.AddScoped<DXPanel.Services.Nodes.INodeSshService, DXPanel.Services.Nodes.NodeSshService>();
builder.Services.AddScoped<DXPanel.Services.Nodes.INodeManagerService, DXPanel.Services.Nodes.NodeManagerService>();
builder.Services.AddScoped<DXPanel.Services.Nodes.INodeMigrationService, DXPanel.Services.Nodes.NodeMigrationService>();
builder.Services.AddHostedService<DXPanel.Services.Nodes.NodeMonitorService>();

// Phase 12 — VPS provisioning (Proxmox)
builder.Services.AddSingleton<DXPanel.Services.Vps.IVpsBroadcast, DXPanel.Services.Vps.VpsBroadcast>();
builder.Services.AddScoped<DXPanel.Services.Vps.IProxmoxService, DXPanel.Services.Vps.ProxmoxService>();
builder.Services.AddScoped<DXPanel.Services.Vps.IVpsManagerService, DXPanel.Services.Vps.VpsManagerService>();
builder.Services.AddScoped<DXPanel.Services.Vps.IVpsProvisioningService, DXPanel.Services.Vps.VpsProvisioningService>();
builder.Services.AddHostedService<DXPanel.Services.Vps.VpsMetricsService>();

// Phase 15 — email server management + queue + blacklist
builder.Services.AddSingleton<DXPanel.Services.Email.IEmailBroadcast, DXPanel.Services.Email.EmailBroadcast>();
builder.Services.AddScoped<DXPanel.Services.Email.IEmailQueueService, DXPanel.Services.Email.EmailQueueService>();
builder.Services.AddScoped<DXPanel.Services.Email.IBlacklistService, DXPanel.Services.Email.BlacklistService>();
builder.Services.AddScoped<DXPanel.Services.Email.IEmailLogService, DXPanel.Services.Email.EmailLogService>();
builder.Services.AddScoped<DXPanel.Services.Email.IMailServerService, DXPanel.Services.Email.MailServerService>();
builder.Services.AddScoped<DXPanel.Services.Email.IBounceHandlerService, DXPanel.Services.Email.BounceHandlerService>();
builder.Services.AddScoped<DXPanel.Services.Email.IDeliverabilityService, DXPanel.Services.Email.DeliverabilityService>();
builder.Services.AddHostedService<DXPanel.Services.Email.EmailQueueProcessor>();
builder.Services.AddHostedService<DXPanel.Services.Email.BounceMonitorService>();

// Phase 16 — Node.js/Python/Ruby/Go app hosting
builder.Services.AddSingleton<DXPanel.Services.AppHosting.IHostedAppBroadcast, DXPanel.Services.AppHosting.HostedAppBroadcast>();
builder.Services.AddScoped<DXPanel.Services.AppHosting.IPortManagerService, DXPanel.Services.AppHosting.PortManagerService>();
builder.Services.AddScoped<DXPanel.Services.AppHosting.IRuntimeService, DXPanel.Services.AppHosting.RuntimeService>();
builder.Services.AddScoped<DXPanel.Services.AppHosting.IPm2Service, DXPanel.Services.AppHosting.Pm2Service>();
builder.Services.AddScoped<DXPanel.Services.AppHosting.IGunicornService, DXPanel.Services.AppHosting.GunicornService>();
builder.Services.AddScoped<DXPanel.Services.AppHosting.IHostedAppService, DXPanel.Services.AppHosting.HostedAppService>();
builder.Services.AddHostedService<DXPanel.Services.AppHosting.AppHealthMonitor>();

// Google OAuth login. Only registered when credentials are configured so an
// empty appsettings block doesn't crash startup (the Google handler requires a
// non-empty ClientId/ClientSecret).
var googleClientId = builder.Configuration["Google:ClientId"];
var googleClientSecret = builder.Configuration["Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    builder.Services.AddAuthentication().AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        options.SignInScheme = IdentityConstants.ExternalScheme;
    });
}

// JWT bearer for the REST API (in addition to the Identity cookie).
builder.Services.AddAuthentication().AddJwtBearer("Bearer", options =>
{
    options.TokenValidationParameters = DXPanel.Services.Api.JwtTokenService.BuildKey(builder.Configuration) is var key
        ? new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = DXPanel.Services.Api.JwtTokenService.Issuer,
            ValidAudience = DXPanel.Services.Api.JwtTokenService.Audience,
            IssuerSigningKey = key
        }
        : throw new InvalidOperationException();
});

// Allow up to 100 MB uploads for the file manager
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 100L * 1024 * 1024;
});
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 100L * 1024 * 1024);

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SuperAdminOnly", policy => policy.RequireRole(Roles.SuperAdmin));
    options.AddPolicy("SuperAdminOrReseller", policy => policy.RequireRole(Roles.SuperAdmin, Roles.Reseller));
});

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Dashboard");
    options.Conventions.AuthorizeFolder("/Users", "SuperAdminOrReseller");
    options.Conventions.AuthorizeFolder("/Domains");
    options.Conventions.AuthorizeFolder("/Packages", "SuperAdminOnly");
    options.Conventions.AuthorizeFolder("/Databases");
    options.Conventions.AuthorizeFolder("/Ftp");
    options.Conventions.AuthorizeFolder("/Email");
    options.Conventions.AuthorizeFolder("/Ssl");
    options.Conventions.AuthorizeFolder("/Dns");
    options.Conventions.AuthorizeFolder("/FileManager");
    options.Conventions.AuthorizeFolder("/Notifications");
    options.Conventions.AuthorizeFolder("/Settings", "SuperAdminOnly");
    options.Conventions.AuthorizeFolder("/Billing");
    options.Conventions.AuthorizeFolder("/Checkout");
    options.Conventions.AuthorizeFolder("/Client");
    options.Conventions.AuthorizeFolder("/Reseller", "SuperAdminOrReseller");
    options.Conventions.AuthorizeFolder("/Affiliate");
    options.Conventions.AuthorizeFolder("/Admin", "SuperAdminOnly");
    // The public domain-checker page (/Domains.cshtml) owns the bare "/domains"
    // route; drop the folder-index's implicit bare route so it only serves
    // "/Domains/Index" (which the authenticated nav links to explicitly).
    options.Conventions.AddPageRouteModelConvention("/Domains/Index", model =>
    {
        var bare = model.Selectors.FirstOrDefault(s =>
            string.Equals(s.AttributeRouteModel?.Template, "Domains", StringComparison.OrdinalIgnoreCase));
        if (bare != null) model.Selectors.Remove(bare);
    });

    options.Conventions.AllowAnonymousToPage("/Docs/Whmcs");
    options.Conventions.AllowAnonymousToPage("/Docs/Index");

    // /docs/api is public only when the operator opts in; otherwise it needs a login.
    if (builder.Configuration.GetValue<bool>("PanelSettings:DeveloperDocsPublic"))
        options.Conventions.AllowAnonymousToPage("/Docs/Api");
    else
        options.Conventions.AuthorizePage("/Docs/Api");
    options.Conventions.AllowAnonymousToPage("/Index");
    options.Conventions.AllowAnonymousToPage("/Account/Login");
    options.Conventions.AllowAnonymousToPage("/Account/AccessDenied");
    options.Conventions.AllowAnonymousToPage("/Pricing");
})
// Turn delete/remove handler failures into a friendly flash instead of an error page.
.AddMvcOptions(options => options.Filters.Add<DXPanel.Services.DeleteErrorPageFilter>());

var app = builder.Build();

// Behind a reverse proxy: rewrite scheme/remote-IP from X-Forwarded-* before any
// middleware inspects them. Must be first.
if (behindProxy)
    app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// The reverse proxy terminates TLS; only self-redirect to HTTPS when we're the edge.
if (!behindProxy)
    app.UseHttpsRedirection();

app.UseStaticFiles();

// Phase 11 — the browser terminal connects over a WebSocket.
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Resolve white-label branding for the current request (after auth so the
// logged-in user's reseller can be detected).
app.UseMiddleware<DXPanel.Services.Reseller.BrandingMiddleware>();

app.MapRazorPages();

// Stripe webhook endpoint (anonymous; signature-verified inside the gateway).
app.MapPost("/api/stripe/webhook", async (HttpRequest request,
    DXPanel.Services.Billing.IStripeGateway stripe,
    DXPanel.Services.Billing.IBillingService billing,
    DXPanel.Data.ApplicationDbContext db) =>
{
    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync();
    var signature = request.Headers["Stripe-Signature"].FirstOrDefault();

    var evt = stripe.ConstructWebhookEvent(json, signature);
    if (evt == null)
    {
        return Results.BadRequest(new { error = "Invalid signature" });
    }

    evt.Data.TryGetValue("id", out var objId);
    // For invoice.* events the subscription id is on the invoice object.
    evt.Data.TryGetValue("subscription", out var subId);
    var stripeSubId = subId ?? objId;

    switch (evt.Type)
    {
        case "checkout.session.completed":
        case "invoice.payment_succeeded":
            await billing.HandlePaymentSucceededAsync(stripeSubId);
            break;
        case "invoice.payment_failed":
            await billing.HandlePaymentFailedAsync(stripeSubId);
            break;
        case "customer.subscription.deleted":
            await billing.HandleSubscriptionDeletedAsync(objId);
            break;
        case "customer.subscription.updated":
            int? newPlanId = null;
            if (evt.Data.TryGetValue("planId", out var pid) && int.TryParse(pid, out var parsed)) newPlanId = parsed;
            await billing.HandleSubscriptionUpdatedAsync(objId, newPlanId);
            break;
    }

    return Results.Ok(new { received = true, type = evt.Type });
}).AllowAnonymous();

// Phase 6B — integration + REST APIs + affiliate referral tracking
app.MapReferralTracking();
app.MapProvisioningApis();
app.MapRestApi();

// Phase 7 — health check
app.MapHealthCheck();

// Phase 8 — public site endpoints (lang, robots.txt, sitemap.xml)
app.MapPublicSite();

// Phase 9 — security real-time hub
app.MapHub<DXPanel.Services.Security.SecurityHub>("/hubs/security");

// Phase 10 — application install progress hub
app.MapHub<DXPanel.Services.Apps.InstallHub>("/hubs/install");

// Phase 11 — developer tools: real-time hub, git webhooks, browser terminal, OpenAPI document
app.MapHub<DXPanel.Services.Developer.DevToolsHub>("/hubs/devtools");
app.MapGitWebhook();
app.MapTerminalWebSocket();
app.MapOpenApi();

// Phase 14 — node fleet real-time hub (metrics + migration progress)
app.MapHub<DXPanel.Services.Nodes.NodeHub>("/hubs/nodes");

// Phase 12 — VPS provisioning + live stats hub
app.MapHub<DXPanel.Services.Vps.VpsHub>("/hubs/vps");

// Phase 12 — VPS web console: noVNC WebSocket proxy (/ws/vnc/{instanceId}).
app.MapVncProxy();

// Phase 15 — mail queue real-time hub
app.MapHub<DXPanel.Services.Email.EmailHub>("/hubs/email");

// Phase 16 — hosted app metrics + logs + deploy hub
app.MapHub<DXPanel.Services.AppHosting.HostedAppHub>("/hubs/hostedapps");

// Phase 13 — WordPress calls this on post publish to purge the Cloudflare cache.
// Authenticated by the domain's webhook secret (the panel API key prefix of the owner).
app.MapPost("/api/cloudflare/wp-purge/{domainId:int}", async (
    int domainId, HttpRequest request,
    DXPanel.Services.Cloudflare.ICloudflareManager cloudflare,
    DXPanel.Data.ApplicationDbContext db) =>
{
    // The WP plugin sends the panel API key; verify it belongs to the domain's owner.
    var apiKey = request.Headers["X-DX-Key"].FirstOrDefault();
    if (string.IsNullOrEmpty(apiKey)) return Results.Unauthorized();

    var domain = await db.Domains.FirstOrDefaultAsync(d => d.Id == domainId);
    if (domain == null) return Results.NotFound();

    var prefix = apiKey.Length >= 16 ? apiKey[..16] : apiKey;
    var keyOwned = await db.ApiKeys.AnyAsync(k => k.UserId == domain.UserId && k.Prefix == prefix && k.IsActive);
    if (!keyOwned) return Results.Unauthorized();

    var result = await cloudflare.PurgeOnPublishAsync(domainId);
    return Results.Ok(new { purged = result.Success, message = result.Message });
}).AllowAnonymous();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(scope.ServiceProvider);
}

app.Run();
