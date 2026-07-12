using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Api;
using SRXPanel.Services.Developer;
using SRXPanel.Services.Public;
using SRXPanel.Services.Vps;

var builder = WebApplication.CreateBuilder(args);

// Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=srxpanel.db";
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
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<ISystemStatsService, SystemStatsService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IUserScopeService, UserScopeService>();
builder.Services.AddSingleton<ISecretHasher, BCryptSecretHasher>();
builder.Services.AddSingleton<IRateLimitService, RateLimitService>();
builder.Services.AddSingleton<IFileManagerService, FileManagerService>();

// Phase 3 — Linux service integration (simulation-aware)
builder.Services.Configure<SRXPanel.Services.PanelSettings>(builder.Configuration.GetSection("Panel"));
builder.Services.AddScoped<SRXPanel.Services.ISettingsWriter, SRXPanel.Services.SettingsWriter>();
builder.Services.AddScoped<SRXPanel.Services.Interfaces.ICommandRunner, SRXPanel.Services.Integration.CommandRunner>();
builder.Services.AddScoped<SRXPanel.Services.Interfaces.INginxService, SRXPanel.Services.Integration.NginxService>();
builder.Services.AddScoped<SRXPanel.Services.Interfaces.IMySqlService, SRXPanel.Services.Integration.MySqlService>();
builder.Services.AddScoped<SRXPanel.Services.Interfaces.IDnsService, SRXPanel.Services.Integration.DnsService>();
builder.Services.AddScoped<SRXPanel.Services.Interfaces.IFtpService, SRXPanel.Services.Integration.FtpService>();
builder.Services.AddScoped<SRXPanel.Services.Interfaces.IEmailService, SRXPanel.Services.Integration.EmailService>();
builder.Services.AddScoped<SRXPanel.Services.Interfaces.ISslService, SRXPanel.Services.Integration.SslService>();

// SMS (Twilio) — real send in production, logged in simulation.
builder.Services.Configure<SRXPanel.Services.TwilioSettings>(builder.Configuration.GetSection("Twilio"));
builder.Services.AddScoped<SRXPanel.Services.Integration.ITwilioService, SRXPanel.Services.Integration.TwilioService>();

// Off-site backup (S3 / Backblaze B2) — real upload in production, logged in simulation.
builder.Services.Configure<SRXPanel.Services.BackupSettings>(builder.Configuration.GetSection("Backup"));
builder.Services.AddScoped<SRXPanel.Services.Integration.IOffSiteBackupService, SRXPanel.Services.Integration.OffSiteBackupService>();

// Exchange rates — daily refresh from a free API, cached in the ExchangeRate table.
builder.Services.AddHttpClient("exchange", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("SRXPanel-ExchangeRates/1.0");
});
builder.Services.AddScoped<SRXPanel.Services.Integration.IExchangeRateService, SRXPanel.Services.Integration.ExchangeRateService>();
builder.Services.AddHostedService<SRXPanel.Services.Integration.RefreshExchangeRatesJob>();

// Phase 4 — Payments, provisioning, billing
builder.Services.Configure<SRXPanel.Services.StripeSettings>(builder.Configuration.GetSection("Stripe"));
builder.Services.AddScoped<SRXPanel.Services.Billing.IStripeGateway, SRXPanel.Services.Billing.StripeGateway>();
builder.Services.AddScoped<SRXPanel.Services.Billing.IMailerService, SRXPanel.Services.Billing.MailerService>();
builder.Services.AddScoped<SRXPanel.Services.Billing.IProvisioningService, SRXPanel.Services.Billing.ProvisioningService>();
builder.Services.AddScoped<SRXPanel.Services.Billing.IBillingService, SRXPanel.Services.Billing.BillingService>();
builder.Services.AddHostedService<SRXPanel.Services.Billing.TrialDunningBackgroundService>();

// Phase 5 — Customer self-service portal
builder.Services.AddScoped<SRXPanel.Services.Portal.ITicketService, SRXPanel.Services.Portal.TicketService>();
builder.Services.AddScoped<SRXPanel.Services.Portal.IBackupService, SRXPanel.Services.Portal.BackupService>();
builder.Services.AddScoped<SRXPanel.Services.Portal.IApiKeyService, SRXPanel.Services.Portal.ApiKeyService>();

// Phase 6A — reseller system + white-label
builder.Services.AddScoped<SRXPanel.Services.Reseller.IResellerService, SRXPanel.Services.Reseller.ResellerService>();
builder.Services.AddScoped<SRXPanel.Services.Reseller.IResourceGuard, SRXPanel.Services.Reseller.ResourceGuard>();
builder.Services.AddScoped<SRXPanel.Services.Reseller.IBrandingResolver, SRXPanel.Services.Reseller.BrandingResolver>();

// Phase 6B — reseller billing + integrations
builder.Services.AddScoped<SRXPanel.Services.IPlatformSettingsService, SRXPanel.Services.PlatformSettingsService>();
builder.Services.AddScoped<SRXPanel.Services.Billing.IResellerBillingService, SRXPanel.Services.Billing.ResellerBillingService>();
builder.Services.AddScoped<SRXPanel.Services.Reseller.ICurrencyService, SRXPanel.Services.Reseller.CurrencyService>();
builder.Services.AddScoped<SRXPanel.Services.Reseller.IAffiliateService, SRXPanel.Services.Reseller.AffiliateService>();
builder.Services.AddScoped<SRXPanel.Services.Api.IApiAuthService, SRXPanel.Services.Api.ApiAuthService>();
builder.Services.AddScoped<SRXPanel.Services.Api.IApiAccountService, SRXPanel.Services.Api.ApiAccountService>();
builder.Services.AddScoped<SRXPanel.Services.Api.IWebhookDispatcher, SRXPanel.Services.Api.WebhookDispatcher>();
builder.Services.AddSingleton<SRXPanel.Services.Api.IJwtTokenService, SRXPanel.Services.Api.JwtTokenService>();

// Phase 7 — version management + health check
builder.Services.AddScoped<SRXPanel.Services.IUpdateService, SRXPanel.Services.UpdateService>();

// Phase 8 — public frontend
builder.Services.AddScoped<SRXPanel.Services.IFrontendService, SRXPanel.Services.FrontendService>();
builder.Services.Configure<SRXPanel.Services.PublicSiteOptions>(builder.Configuration.GetSection("PanelSettings"));

// Store / upgrade system
builder.Services.AddScoped<SRXPanel.Services.Store.ISmsSender, SRXPanel.Services.Store.SmsSender>();
builder.Services.AddScoped<SRXPanel.Services.Store.IStoreService, SRXPanel.Services.Store.StoreService>();
builder.Services.AddHostedService<SRXPanel.Services.Store.InvoiceReminderService>();

// Phase 9 — security package
builder.Services.AddSignalR();
builder.Services.AddSingleton<SRXPanel.Services.Security.ISecurityBroadcast, SRXPanel.Services.Security.SecurityBroadcast>();
builder.Services.AddScoped<SRXPanel.Services.Security.IModSecurityService, SRXPanel.Services.Security.ModSecurityService>();
builder.Services.AddScoped<SRXPanel.Services.Security.IClamAvService, SRXPanel.Services.Security.ClamAvService>();
builder.Services.AddScoped<SRXPanel.Services.Security.IMalwareScanner, SRXPanel.Services.Security.MalwareScannerService>();
builder.Services.AddScoped<SRXPanel.Services.Security.IEmailSecurityService, SRXPanel.Services.Security.EmailSecurityService>();
builder.Services.AddScoped<SRXPanel.Services.Security.IBruteForceService, SRXPanel.Services.Security.BruteForceProtectionService>();
builder.Services.AddScoped<SRXPanel.Services.Security.IIpManagerService, SRXPanel.Services.Security.IpManagerService>();
builder.Services.AddScoped<SRXPanel.Services.Security.ISecurityScoreService, SRXPanel.Services.Security.SecurityScoreService>();

// Phase 10 — one-click application installer
builder.Services.AddSingleton<SRXPanel.Services.Apps.IInstallBroadcast, SRXPanel.Services.Apps.InstallBroadcast>();
builder.Services.AddScoped<SRXPanel.Services.Apps.IAppInstallerService, SRXPanel.Services.Apps.AppInstallerService>();
builder.Services.AddScoped<SRXPanel.Services.Apps.IWordPressManager, SRXPanel.Services.Apps.WordPressManagerService>();

// Phase 11 — developer tools
builder.Services.AddSingleton<SRXPanel.Services.Developer.IDevToolsBroadcast, SRXPanel.Services.Developer.DevToolsBroadcast>();
builder.Services.AddScoped<SRXPanel.Services.Developer.ICronService, SRXPanel.Services.Developer.CronService>();
builder.Services.AddScoped<SRXPanel.Services.Developer.ISshKeyService, SRXPanel.Services.Developer.SshKeyService>();
builder.Services.AddScoped<SRXPanel.Services.Developer.IGitDeployService, SRXPanel.Services.Developer.GitDeployService>();
builder.Services.AddScoped<SRXPanel.Services.Developer.ITerminalService, SRXPanel.Services.Developer.TerminalService>();
builder.Services.AddScoped<SRXPanel.Services.Developer.IPackageManagerService, SRXPanel.Services.Developer.PackageManagerService>();
builder.Services.AddScoped<SRXPanel.Services.Developer.ILogViewerService, SRXPanel.Services.Developer.LogViewerService>();
builder.Services.AddScoped<SRXPanel.Services.Developer.IPhpConfigService, SRXPanel.Services.Developer.PhpConfigService>();
builder.Services.AddScoped<SRXPanel.Services.Developer.IStagingService, SRXPanel.Services.Developer.StagingService>();
builder.Services.AddScoped<SRXPanel.Services.Developer.IDnsLookupService, SRXPanel.Services.Developer.DnsLookupService>();
builder.Services.AddScoped<SRXPanel.Services.Developer.IPerformanceService, SRXPanel.Services.Developer.PerformanceService>();
builder.Services.AddScoped<SRXPanel.Services.Developer.IDatabaseToolsService, SRXPanel.Services.Developer.DatabaseToolsService>();
builder.Services.AddScoped<SRXPanel.Services.Developer.IDeveloperSettingsService, SRXPanel.Services.Developer.DeveloperSettingsService>();
builder.Services.AddHostedService<SRXPanel.Services.Developer.CronBackgroundService>();
builder.Services.AddHostedService<SRXPanel.Services.Developer.DeveloperMaintenanceService>();

// Outbound HTTP for the performance tester and webhook deliveries. Both talk to
// untrusted third-party hosts, so they get short timeouts and no redirect following.
builder.Services.AddHttpClient("perf", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("SRXPanel-PerformanceTest/1.0");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

builder.Services.AddHttpClient("webhook", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("SRXPanel-Webhook/1.0");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

// Phase 11 — OpenAPI document backing the Swagger UI at /docs/api
builder.Services.AddOpenApi();

// Phase 13 — Cloudflare integration
builder.Services.AddHttpClient("cloudflare", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("SRXPanel-Cloudflare/1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
builder.Services.AddScoped<SRXPanel.Services.Cloudflare.ICloudflareService, SRXPanel.Services.Cloudflare.CloudflareService>();
builder.Services.AddScoped<SRXPanel.Services.Cloudflare.ICloudflareManager, SRXPanel.Services.Cloudflare.CloudflareManager>();
builder.Services.AddHostedService<SRXPanel.Services.Cloudflare.CloudflareAnalyticsService>();

// Phase 14 — multi-server + node management
builder.Services.AddSingleton<SRXPanel.Services.Nodes.INodeBroadcast, SRXPanel.Services.Nodes.NodeBroadcast>();
builder.Services.AddScoped<SRXPanel.Services.Nodes.INodeSshService, SRXPanel.Services.Nodes.NodeSshService>();
builder.Services.AddScoped<SRXPanel.Services.Nodes.INodeManagerService, SRXPanel.Services.Nodes.NodeManagerService>();
builder.Services.AddScoped<SRXPanel.Services.Nodes.INodeMigrationService, SRXPanel.Services.Nodes.NodeMigrationService>();
builder.Services.AddHostedService<SRXPanel.Services.Nodes.NodeMonitorService>();

// Phase 12 — VPS provisioning (Proxmox)
builder.Services.AddSingleton<SRXPanel.Services.Vps.IVpsBroadcast, SRXPanel.Services.Vps.VpsBroadcast>();
builder.Services.AddScoped<SRXPanel.Services.Vps.IProxmoxService, SRXPanel.Services.Vps.ProxmoxService>();
builder.Services.AddScoped<SRXPanel.Services.Vps.IVpsManagerService, SRXPanel.Services.Vps.VpsManagerService>();
builder.Services.AddScoped<SRXPanel.Services.Vps.IVpsProvisioningService, SRXPanel.Services.Vps.VpsProvisioningService>();
builder.Services.AddHostedService<SRXPanel.Services.Vps.VpsMetricsService>();

// Phase 15 — email server management + queue + blacklist
builder.Services.AddSingleton<SRXPanel.Services.Email.IEmailBroadcast, SRXPanel.Services.Email.EmailBroadcast>();
builder.Services.AddScoped<SRXPanel.Services.Email.IEmailQueueService, SRXPanel.Services.Email.EmailQueueService>();
builder.Services.AddScoped<SRXPanel.Services.Email.IBlacklistService, SRXPanel.Services.Email.BlacklistService>();
builder.Services.AddScoped<SRXPanel.Services.Email.IEmailLogService, SRXPanel.Services.Email.EmailLogService>();
builder.Services.AddScoped<SRXPanel.Services.Email.IMailServerService, SRXPanel.Services.Email.MailServerService>();
builder.Services.AddScoped<SRXPanel.Services.Email.IBounceHandlerService, SRXPanel.Services.Email.BounceHandlerService>();
builder.Services.AddScoped<SRXPanel.Services.Email.IDeliverabilityService, SRXPanel.Services.Email.DeliverabilityService>();
builder.Services.AddHostedService<SRXPanel.Services.Email.EmailQueueProcessor>();
builder.Services.AddHostedService<SRXPanel.Services.Email.BounceMonitorService>();

// Phase 16 — Node.js/Python/Ruby/Go app hosting
builder.Services.AddSingleton<SRXPanel.Services.AppHosting.IHostedAppBroadcast, SRXPanel.Services.AppHosting.HostedAppBroadcast>();
builder.Services.AddScoped<SRXPanel.Services.AppHosting.IPortManagerService, SRXPanel.Services.AppHosting.PortManagerService>();
builder.Services.AddScoped<SRXPanel.Services.AppHosting.IRuntimeService, SRXPanel.Services.AppHosting.RuntimeService>();
builder.Services.AddScoped<SRXPanel.Services.AppHosting.IPm2Service, SRXPanel.Services.AppHosting.Pm2Service>();
builder.Services.AddScoped<SRXPanel.Services.AppHosting.IGunicornService, SRXPanel.Services.AppHosting.GunicornService>();
builder.Services.AddScoped<SRXPanel.Services.AppHosting.IHostedAppService, SRXPanel.Services.AppHosting.HostedAppService>();
builder.Services.AddHostedService<SRXPanel.Services.AppHosting.AppHealthMonitor>();

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
    options.TokenValidationParameters = SRXPanel.Services.Api.JwtTokenService.BuildKey(builder.Configuration) is var key
        ? new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = SRXPanel.Services.Api.JwtTokenService.Issuer,
            ValidAudience = SRXPanel.Services.Api.JwtTokenService.Audience,
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
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

// Phase 11 — the browser terminal connects over a WebSocket.
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Resolve white-label branding for the current request (after auth so the
// logged-in user's reseller can be detected).
app.UseMiddleware<SRXPanel.Services.Reseller.BrandingMiddleware>();

app.MapRazorPages();

// Stripe webhook endpoint (anonymous; signature-verified inside the gateway).
app.MapPost("/api/stripe/webhook", async (HttpRequest request,
    SRXPanel.Services.Billing.IStripeGateway stripe,
    SRXPanel.Services.Billing.IBillingService billing,
    SRXPanel.Data.ApplicationDbContext db) =>
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
app.MapHub<SRXPanel.Services.Security.SecurityHub>("/hubs/security");

// Phase 10 — application install progress hub
app.MapHub<SRXPanel.Services.Apps.InstallHub>("/hubs/install");

// Phase 11 — developer tools: real-time hub, git webhooks, browser terminal, OpenAPI document
app.MapHub<SRXPanel.Services.Developer.DevToolsHub>("/hubs/devtools");
app.MapGitWebhook();
app.MapTerminalWebSocket();
app.MapOpenApi();

// Phase 14 — node fleet real-time hub (metrics + migration progress)
app.MapHub<SRXPanel.Services.Nodes.NodeHub>("/hubs/nodes");

// Phase 12 — VPS provisioning + live stats hub
app.MapHub<SRXPanel.Services.Vps.VpsHub>("/hubs/vps");

// Phase 12 — VPS web console: noVNC WebSocket proxy (/ws/vnc/{instanceId}).
app.MapVncProxy();

// Phase 15 — mail queue real-time hub
app.MapHub<SRXPanel.Services.Email.EmailHub>("/hubs/email");

// Phase 16 — hosted app metrics + logs + deploy hub
app.MapHub<SRXPanel.Services.AppHosting.HostedAppHub>("/hubs/hostedapps");

// Phase 13 — WordPress calls this on post publish to purge the Cloudflare cache.
// Authenticated by the domain's webhook secret (the panel API key prefix of the owner).
app.MapPost("/api/cloudflare/wp-purge/{domainId:int}", async (
    int domainId, HttpRequest request,
    SRXPanel.Services.Cloudflare.ICloudflareManager cloudflare,
    SRXPanel.Data.ApplicationDbContext db) =>
{
    // The WP plugin sends the panel API key; verify it belongs to the domain's owner.
    var apiKey = request.Headers["X-SRX-Key"].FirstOrDefault();
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
