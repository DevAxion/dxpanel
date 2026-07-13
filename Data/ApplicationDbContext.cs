using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Models;

namespace SRXPanel.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Domain> Domains { get; set; } = null!;
    public DbSet<Package> Packages { get; set; } = null!;
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;
    public DbSet<Database> Databases { get; set; } = null!;
    public DbSet<FtpAccount> FtpAccounts { get; set; } = null!;
    public DbSet<EmailAccount> EmailAccounts { get; set; } = null!;
    public DbSet<EmailForwarder> EmailForwarders { get; set; } = null!;
    public DbSet<SslCertificate> SslCertificates { get; set; } = null!;
    public DbSet<DnsZone> DnsZones { get; set; } = null!;
    public DbSet<DnsRecord> DnsRecords { get; set; } = null!;
    public DbSet<Notification> Notifications { get; set; } = null!;
    public DbSet<CommandLog> CommandLogs { get; set; } = null!;
    public DbSet<Plan> Plans { get; set; } = null!;
    public DbSet<Subscription> Subscriptions { get; set; } = null!;
    public DbSet<Invoice> Invoices { get; set; } = null!;
    public DbSet<PaymentMethod> PaymentMethods { get; set; } = null!;
    public DbSet<Coupon> Coupons { get; set; } = null!;
    public DbSet<Ticket> Tickets { get; set; } = null!;
    public DbSet<TicketReply> TicketReplies { get; set; } = null!;
    public DbSet<CannedResponse> CannedResponses { get; set; } = null!;
    public DbSet<Subdomain> Subdomains { get; set; } = null!;
    public DbSet<DomainRedirect> DomainRedirects { get; set; } = null!;
    public DbSet<Backup> Backups { get; set; } = null!;
    public DbSet<BackupSchedule> BackupSchedules { get; set; } = null!;
    public DbSet<ApiKey> ApiKeys { get; set; } = null!;

    // Phase 6A — reseller system + white-label
    public DbSet<ResellerProfile> ResellerProfiles { get; set; } = null!;
    public DbSet<ResellerBranding> ResellerBrandings { get; set; } = null!;
    public DbSet<ResellerPackage> ResellerPackages { get; set; } = null!;
    public DbSet<ImpersonationSession> ImpersonationSessions { get; set; } = null!;

    // Phase 6B — reseller billing + integrations
    public DbSet<ResellerBillingConfig> ResellerBillingConfigs { get; set; } = null!;
    public DbSet<ResellerTransaction> ResellerTransactions { get; set; } = null!;
    public DbSet<ResellerInvoice> ResellerInvoices { get; set; } = null!;
    public DbSet<ResellerPaymentSettings> ResellerPaymentSettings { get; set; } = null!;
    public DbSet<ResellerInvoiceSettings> ResellerInvoiceSettings { get; set; } = null!;
    public DbSet<ExchangeRate> ExchangeRates { get; set; } = null!;
    public DbSet<Currency> Currencies { get; set; } = null!;
    public DbSet<Affiliate> Affiliates { get; set; } = null!;
    public DbSet<AffiliateReferral> AffiliateReferrals { get; set; } = null!;
    public DbSet<AffiliatePayoutRequest> AffiliatePayoutRequests { get; set; } = null!;
    public DbSet<AffiliateClick> AffiliateClicks { get; set; } = null!;
    public DbSet<ApiRequestLog> ApiRequestLogs { get; set; } = null!;
    public DbSet<WebhookEndpoint> WebhookEndpoints { get; set; } = null!;
    public DbSet<PlatformSettings> PlatformSettings { get; set; } = null!;

    // Phase 7 — version management
    public DbSet<UpdateHistory> UpdateHistory { get; set; } = null!;

    // Store / upgrade system
    public DbSet<Addon> Addons { get; set; } = null!;
    public DbSet<ClientAddon> ClientAddons { get; set; } = null!;
    public DbSet<ClientService> ClientServices { get; set; } = null!;
    public DbSet<CartItem> CartItems { get; set; } = null!;
    public DbSet<DomainRegistration> DomainRegistrations { get; set; } = null!;

    // Phase 10 — one-click application installer
    public DbSet<AppDefinition> AppDefinitions { get; set; } = null!;
    public DbSet<AppInstallation> AppInstallations { get; set; } = null!;
    public DbSet<AppInstallJob> AppInstallJobs { get; set; } = null!;
    public DbSet<WpAsset> WpAssets { get; set; } = null!;
    public DbSet<AppUpdateSettings> AppUpdateSettings { get; set; } = null!;

    // Phase 14 — multi-server + node management
    public DbSet<ServerNode> ServerNodes { get; set; } = null!;
    public DbSet<ServerService> ServerServices { get; set; } = null!;
    public DbSet<ServerMetric> ServerMetrics { get; set; } = null!;
    public DbSet<DomainNode> DomainNodes { get; set; } = null!;
    public DbSet<DatabaseNode> DatabaseNodes { get; set; } = null!;
    public DbSet<UserNode> UserNodes { get; set; } = null!;
    public DbSet<NodeAlert> NodeAlerts { get; set; } = null!;
    public DbSet<LoadBalancerSettings> LoadBalancerSettings { get; set; } = null!;

    // Phase 12 — VPS provisioning (Proxmox)
    public DbSet<ProxmoxNode> ProxmoxNodes { get; set; } = null!;
    public DbSet<VpsTemplate> VpsTemplates { get; set; } = null!;
    public DbSet<VpsInstance> VpsInstances { get; set; } = null!;
    public DbSet<VpsAction> VpsActions { get; set; } = null!;
    public DbSet<VpsBackup> VpsBackups { get; set; } = null!;
    public DbSet<VpsSnapshot> VpsSnapshots { get; set; } = null!;
    public DbSet<VpsConsoleSession> VpsConsoleSessions { get; set; } = null!;
    public DbSet<VpsMetric> VpsMetrics { get; set; } = null!;
    public DbSet<VpsIpAddress> VpsIpAddresses { get; set; } = null!;
    public DbSet<VpsFirewallRule> VpsFirewallRules { get; set; } = null!;

    // Phase 15 — email server management + queue + blacklist
    public DbSet<EmailQueue> EmailQueues { get; set; } = null!;
    public DbSet<EmailQueueStats> EmailQueueStats { get; set; } = null!;
    public DbSet<BlacklistEntry> BlacklistEntries { get; set; } = null!;
    public DbSet<BlacklistCheck> BlacklistChecks { get; set; } = null!;
    public DbSet<EmailBounce> EmailBounces { get; set; } = null!;
    public DbSet<EmailLog> EmailLogs { get; set; } = null!;
    public DbSet<MailServerConfig> MailServerConfigs { get; set; } = null!;

    // Phase 16 — Node.js/Python/Ruby/Go app hosting
    public DbSet<AppRuntime> AppRuntimes { get; set; } = null!;
    public DbSet<HostedApp> HostedApps { get; set; } = null!;
    public DbSet<HostedAppLog> HostedAppLogs { get; set; } = null!;
    public DbSet<HostedAppMetric> HostedAppMetrics { get; set; } = null!;
    public DbSet<HostedAppEnv> HostedAppEnvs { get; set; } = null!;
    public DbSet<HostedAppDeploy> HostedAppDeploys { get; set; } = null!;
    public DbSet<HostedAppHealthIncident> HostedAppHealthIncidents { get; set; } = null!;

    // Phase 13 — Cloudflare integration
    public DbSet<CloudflareAccount> CloudflareAccounts { get; set; } = null!;
    public DbSet<CloudflareDomain> CloudflareDomains { get; set; } = null!;
    public DbSet<CloudflareRule> CloudflareRules { get; set; } = null!;
    public DbSet<CloudflareAnalytics> CloudflareAnalytics { get; set; } = null!;
    public DbSet<CloudflareCache> CloudflareCaches { get; set; } = null!;
    public DbSet<CloudflareTunnel> CloudflareTunnels { get; set; } = null!;

    // Phase 11 — developer tools
    public DbSet<CronJob> CronJobs { get; set; } = null!;
    public DbSet<CronJobLog> CronJobLogs { get; set; } = null!;
    public DbSet<SshKey> SshKeys { get; set; } = null!;
    public DbSet<SshAccess> SshAccesses { get; set; } = null!;
    public DbSet<SshAccessLog> SshAccessLogs { get; set; } = null!;
    public DbSet<GitRepository> GitRepositories { get; set; } = null!;
    public DbSet<GitDeployment> GitDeployments { get; set; } = null!;
    public DbSet<TerminalSession> TerminalSessions { get; set; } = null!;
    public DbSet<PhpConfig> PhpConfigs { get; set; } = null!;
    public DbSet<StagingSite> StagingSites { get; set; } = null!;
    public DbSet<DeveloperSettings> DeveloperSettings { get; set; } = null!;
    public DbSet<WebhookDelivery> WebhookDeliveries { get; set; } = null!;

    // Phase 9 — security package
    public DbSet<WafConfig> WafConfigs { get; set; } = null!;
    public DbSet<WafCustomRule> WafCustomRules { get; set; } = null!;
    public DbSet<WafIpRule> WafIpRules { get; set; } = null!;
    public DbSet<ModSecurityAlert> ModSecurityAlerts { get; set; } = null!;
    public DbSet<ScanResult> ScanResults { get; set; } = null!;
    public DbSet<QuarantinedFile> QuarantinedFiles { get; set; } = null!;
    public DbSet<EmailSecurity> EmailSecurities { get; set; } = null!;
    public DbSet<MalwareScanResult> MalwareScanResults { get; set; } = null!;
    public DbSet<LoginAttempt> LoginAttempts { get; set; } = null!;
    public DbSet<BlockedIP> BlockedIPs { get; set; } = null!;
    public DbSet<IpAccessRule> IpAccessRules { get; set; } = null!;
    public DbSet<SecuritySettings> SecuritySettings { get; set; } = null!;

    // Phase 8 — public frontend + blog + knowledge base
    public DbSet<FrontendSettings> FrontendSettings { get; set; } = null!;
    public DbSet<Testimonial> Testimonials { get; set; } = null!;
    public DbSet<FeatureItem> FeatureItems { get; set; } = null!;
    public DbSet<StatCounter> StatCounters { get; set; } = null!;
    public DbSet<ContactMessage> ContactMessages { get; set; } = null!;
    public DbSet<BlogPost> BlogPosts { get; set; } = null!;
    public DbSet<BlogCategory> BlogCategories { get; set; } = null!;
    public DbSet<BlogTag> BlogTags { get; set; } = null!;
    public DbSet<KbArticle> KbArticles { get; set; } = null!;
    public DbSet<KbCategory> KbCategories { get; set; } = null!;
    public DbSet<VpsPlan> VpsPlans { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(b =>
        {
            b.HasOne(u => u.Reseller)
                .WithMany()
                .HasForeignKey(u => u.ResellerId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(u => u.Package)
                .WithMany(p => p.Users)
                .HasForeignKey(u => u.PackageId)
                .OnDelete(DeleteBehavior.SetNull);

            b.HasOne(u => u.ResellerPackage)
                .WithMany(p => p.Clients)
                .HasForeignKey(u => u.ResellerPackageId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Domain>(b =>
        {
            b.HasOne(d => d.User)
                .WithMany(u => u.Domains)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(d => d.DomainName).IsUnique();
        });

        builder.Entity<Database>(b =>
        {
            b.HasOne(d => d.User)
                .WithMany(u => u.Databases)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(d => d.Domain)
                .WithMany()
                .HasForeignKey(d => d.DomainId)
                .OnDelete(DeleteBehavior.SetNull);

            b.HasIndex(d => d.DbName).IsUnique();
        });

        builder.Entity<FtpAccount>(b =>
        {
            b.HasOne(f => f.User)
                .WithMany(u => u.FtpAccounts)
                .HasForeignKey(f => f.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(f => f.Domain)
                .WithMany()
                .HasForeignKey(f => f.DomainId)
                .OnDelete(DeleteBehavior.SetNull);

            b.HasIndex(f => f.Username).IsUnique();
        });

        builder.Entity<EmailAccount>(b =>
        {
            b.HasOne(e => e.User)
                .WithMany(u => u.EmailAccounts)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(e => e.Domain)
                .WithMany()
                .HasForeignKey(e => e.DomainId)
                .OnDelete(DeleteBehavior.SetNull);

            b.HasIndex(e => e.EmailAddress).IsUnique();
        });

        builder.Entity<EmailForwarder>(b =>
        {
            b.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(e => e.Domain)
                .WithMany()
                .HasForeignKey(e => e.DomainId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<SslCertificate>(b =>
        {
            b.HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(s => s.Domain)
                .WithMany()
                .HasForeignKey(s => s.DomainId)
                .OnDelete(DeleteBehavior.Cascade);

            b.Ignore(s => s.DaysUntilExpiry);
        });

        builder.Entity<DnsZone>(b =>
        {
            b.HasOne(z => z.User)
                .WithMany()
                .HasForeignKey(z => z.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(z => z.Domain)
                .WithMany()
                .HasForeignKey(z => z.DomainId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(z => z.DomainId).IsUnique();
        });

        builder.Entity<DnsRecord>(b =>
        {
            b.HasOne(r => r.Zone)
                .WithMany(z => z.Records)
                .HasForeignKey(r => r.ZoneId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Notification>(b =>
        {
            b.HasOne(n => n.User)
                .WithMany(u => u.Notifications)
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(n => new { n.UserId, n.IsRead });
        });

        builder.Entity<CommandLog>(b =>
        {
            b.HasIndex(c => c.ExecutedAt);
        });

        builder.Entity<Plan>(b =>
        {
            b.Property(p => p.Price).HasColumnType("decimal(18,2)");
            b.Ignore(p => p.AnnualPrice);
        });

        builder.Entity<Subscription>(b =>
        {
            b.HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(s => s.Plan)
                .WithMany()
                .HasForeignKey(s => s.PlanId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(s => s.Coupon)
                .WithMany()
                .HasForeignKey(s => s.CouponId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Invoice>(b =>
        {
            b.Property(i => i.Amount).HasColumnType("decimal(18,2)");

            b.HasOne(i => i.User)
                .WithMany()
                .HasForeignKey(i => i.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(i => i.Subscription)
                .WithMany()
                .HasForeignKey(i => i.SubscriptionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<PaymentMethod>(b =>
        {
            b.HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Coupon>(b =>
        {
            b.HasIndex(c => c.Code).IsUnique();
            b.Ignore(c => c.IsValid);
        });

        builder.Entity<Ticket>(b =>
        {
            b.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(t => t.AssignedTo).WithMany().HasForeignKey(t => t.AssignedToId).OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(t => new { t.UserId, t.Status });
        });

        builder.Entity<TicketReply>(b =>
        {
            b.HasOne(r => r.Ticket).WithMany(t => t.Replies).HasForeignKey(r => r.TicketId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(r => r.User).WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Subdomain>(b =>
        {
            b.HasOne(s => s.Domain).WithMany().HasForeignKey(s => s.DomainId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(s => new { s.DomainId, s.Name }).IsUnique();
        });

        builder.Entity<DomainRedirect>(b =>
        {
            b.HasOne(r => r.Domain).WithMany().HasForeignKey(r => r.DomainId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Backup>(b =>
        {
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.UserId, x.CreatedAt });
        });

        builder.Entity<BackupSchedule>(b =>
        {
            b.HasIndex(x => x.UserId).IsUnique();
        });

        builder.Entity<ApiKey>(b =>
        {
            b.HasOne(k => k.User).WithMany().HasForeignKey(k => k.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(k => k.Prefix);
        });

        builder.Entity<ResellerProfile>(b =>
        {
            b.HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(p => p.UserId).IsUnique();
        });

        builder.Entity<ResellerBranding>(b =>
        {
            b.HasOne(x => x.Reseller).WithMany().HasForeignKey(x => x.ResellerId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.ResellerId).IsUnique();
            b.HasIndex(x => x.CustomDomain);
        });

        builder.Entity<ResellerPackage>(b =>
        {
            b.Property(x => x.Price).HasColumnType("decimal(18,2)");
            b.HasOne(x => x.Reseller).WithMany().HasForeignKey(x => x.ResellerId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.ResellerId);
        });

        // Package / ResellerPackage feature flags: default the store column to true so
        // existing rows (added before this migration) keep the show-everything behavior.
        foreach (var flag in new[]
                 {
                     nameof(Package.AllowVpsStore), nameof(Package.AllowAppHosting),
                     nameof(Package.AllowCloudflare), nameof(Package.AllowAdvancedMail),
                     nameof(Package.AllowDeveloperTools)
                 })
        {
            builder.Entity<Package>().Property<bool>(flag).HasDefaultValue(true);
            builder.Entity<ResellerPackage>().Property<bool>(flag).HasDefaultValue(true);
        }

        builder.Entity<ImpersonationSession>(b =>
        {
            b.HasIndex(x => new { x.ImpersonatorId, x.IsActive });
        });

        // ---------- Phase 6B ----------
        builder.Entity<ResellerBillingConfig>(b =>
        {
            b.Property(x => x.PlatformFeePercent).HasColumnType("decimal(18,2)");
            b.Property(x => x.MinPayoutAmount).HasColumnType("decimal(18,2)");
            b.Property(x => x.AutoTopUpThreshold).HasColumnType("decimal(18,2)");
            b.Property(x => x.AutoTopUpAmount).HasColumnType("decimal(18,2)");
            b.HasOne(x => x.Reseller).WithMany().HasForeignKey(x => x.ResellerId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.ResellerId).IsUnique();
        });

        builder.Entity<ResellerTransaction>(b =>
        {
            b.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            b.Property(x => x.Balance).HasColumnType("decimal(18,2)");
            b.HasOne(x => x.Reseller).WithMany().HasForeignKey(x => x.ResellerId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.ResellerId, x.CreatedAt });
        });

        builder.Entity<ResellerInvoice>(b =>
        {
            b.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            b.HasOne(x => x.Reseller).WithMany().HasForeignKey(x => x.ResellerId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.ResellerId);
        });

        builder.Entity<ResellerPaymentSettings>(b =>
        {
            b.Property(x => x.TaxRatePercent).HasColumnType("decimal(18,2)");
            b.Ignore(x => x.IsConnected);
            b.HasOne(x => x.Reseller).WithMany().HasForeignKey(x => x.ResellerId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.ResellerId).IsUnique();
        });

        builder.Entity<ResellerInvoiceSettings>(b =>
        {
            b.HasOne(x => x.Reseller).WithMany().HasForeignKey(x => x.ResellerId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.ResellerId).IsUnique();
        });

        builder.Entity<ExchangeRate>(b =>
        {
            b.Property(x => x.Rate).HasColumnType("decimal(18,6)");
            b.HasIndex(x => new { x.FromCurrency, x.ToCurrency }).IsUnique();
        });

        builder.Entity<Currency>(b => b.HasIndex(x => x.Code).IsUnique());

        builder.Entity<Affiliate>(b =>
        {
            b.Property(x => x.CommissionPercent).HasColumnType("decimal(18,2)");
            b.Property(x => x.TotalEarned).HasColumnType("decimal(18,2)");
            b.Property(x => x.PendingBalance).HasColumnType("decimal(18,2)");
            b.Property(x => x.PaidBalance).HasColumnType("decimal(18,2)");
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.Code).IsUnique();
            b.HasIndex(x => x.UserId).IsUnique();
        });

        builder.Entity<AffiliateReferral>(b =>
        {
            b.Property(x => x.CommissionAmount).HasColumnType("decimal(18,2)");
            b.HasOne(x => x.Affiliate).WithMany(a => a.Referrals).HasForeignKey(x => x.AffiliateId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AffiliatePayoutRequest>(b =>
        {
            b.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            b.HasOne(x => x.Affiliate).WithMany().HasForeignKey(x => x.AffiliateId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AffiliateClick>(b => b.HasIndex(x => new { x.AffiliateId, x.CreatedAt }));

        builder.Entity<ApiRequestLog>(b => b.HasIndex(x => new { x.KeyPrefix, x.CreatedAt }));

        builder.Entity<WebhookEndpoint>(b =>
        {
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.UserId);
        });

        builder.Entity<PlatformSettings>(b =>
        {
            b.Property(x => x.PlatformFeePercent).HasColumnType("decimal(18,2)");
            b.Property(x => x.MinPayoutAmount).HasColumnType("decimal(18,2)");
            b.Property(x => x.DefaultAffiliateCommission).HasColumnType("decimal(18,2)");
        });

        builder.Entity<UpdateHistory>(b => b.HasIndex(x => x.CreatedAt));

        // ---------- Store / upgrade system ----------
        builder.Entity<Addon>(b =>
        {
            b.Property(x => x.Price).HasColumnType("decimal(18,2)");
            b.HasIndex(x => x.SortOrder);
        });

        builder.Entity<ClientAddon>(b =>
        {
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Addon).WithMany().HasForeignKey(x => x.AddonId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.UserId, x.IsActive });
        });

        builder.Entity<ClientService>(b =>
        {
            b.Property(x => x.Price).HasColumnType("decimal(18,2)");
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.UserId, x.Status });
        });

        builder.Entity<CartItem>(b =>
        {
            b.Property(x => x.Price).HasColumnType("decimal(18,2)");
            b.Ignore(x => x.LineTotal);
            b.HasIndex(x => x.UserId);
        });

        builder.Entity<DomainRegistration>(b =>
        {
            b.Property(x => x.Price).HasColumnType("decimal(18,2)");
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.DomainName);
        });

        // ---------- Phase 8 ----------
        builder.Entity<Testimonial>(b => b.HasIndex(x => x.SortOrder));
        builder.Entity<FeatureItem>(b => b.HasIndex(x => x.SortOrder));
        builder.Entity<StatCounter>(b => b.HasIndex(x => x.SortOrder));
        builder.Entity<ContactMessage>(b => b.HasIndex(x => x.CreatedAt));

        builder.Entity<BlogCategory>(b => b.HasIndex(x => x.Slug).IsUnique());
        builder.Entity<BlogTag>(b => b.HasIndex(x => x.Slug).IsUnique());

        builder.Entity<BlogPost>(b =>
        {
            b.HasIndex(x => x.Slug).IsUnique();
            b.HasIndex(x => new { x.Status, x.PublishedAt });
            b.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.Category).WithMany(c => c.Posts).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
            b.HasMany(x => x.Tags).WithMany(t => t.Posts);
        });

        builder.Entity<KbCategory>(b =>
        {
            b.HasIndex(x => x.Slug).IsUnique();
            b.HasIndex(x => x.SortOrder);
        });

        builder.Entity<KbArticle>(b =>
        {
            b.HasIndex(x => new { x.CategoryId, x.Slug }).IsUnique();
            b.HasOne(x => x.Category).WithMany(c => c.Articles).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<VpsPlan>(b =>
        {
            b.Property(x => x.Price).HasColumnType("decimal(18,2)");
            b.Ignore(x => x.AnnualPrice);
            b.Ignore(x => x.OsList);
            b.HasIndex(x => x.SortOrder);
        });

        // ---------- Phase 9 — security ----------
        builder.Entity<WafConfig>(b =>
        {
            b.HasOne(x => x.Domain).WithMany().HasForeignKey(x => x.DomainId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.DomainId).IsUnique();
        });
        builder.Entity<WafCustomRule>(b => b.HasIndex(x => x.DomainId));
        builder.Entity<WafIpRule>(b => b.HasIndex(x => new { x.DomainId, x.IP }));
        builder.Entity<ModSecurityAlert>(b =>
        {
            b.HasOne(x => x.Domain).WithMany().HasForeignKey(x => x.DomainId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.DomainId, x.Timestamp });
        });
        builder.Entity<ScanResult>(b => b.HasIndex(x => new { x.UserId, x.ScannedAt }));
        builder.Entity<QuarantinedFile>(b => b.HasIndex(x => new { x.UserId, x.IsDeleted }));
        builder.Entity<EmailSecurity>(b =>
        {
            b.HasOne(x => x.Domain).WithMany().HasForeignKey(x => x.DomainId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.DomainId).IsUnique();
        });
        builder.Entity<MalwareScanResult>(b => b.HasIndex(x => new { x.UserId, x.Status }));
        builder.Entity<LoginAttempt>(b => b.HasIndex(x => new { x.IP, x.Timestamp }));
        builder.Entity<BlockedIP>(b =>
        {
            b.Ignore(x => x.IsActive);
            b.HasIndex(x => x.IP);
        });
        builder.Entity<IpAccessRule>(b => b.HasIndex(x => new { x.Kind, x.Value }));

        // ---------- Phase 10 — application installer ----------
        builder.Entity<AppDefinition>(b =>
        {
            b.HasIndex(x => x.Slug).IsUnique();
            b.Ignore(x => x.FeatureList);
        });

        builder.Entity<AppInstallation>(b =>
        {
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Domain).WithMany().HasForeignKey(x => x.DomainId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.AppDefinition).WithMany().HasForeignKey(x => x.AppDefinitionId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.UserId, x.Status });
        });

        builder.Entity<AppInstallJob>(b => b.HasIndex(x => new { x.UserId, x.StartedAt }));

        builder.Entity<WpAsset>(b =>
        {
            b.HasOne(x => x.Installation).WithMany().HasForeignKey(x => x.InstallationId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.InstallationId, x.Type });
        });

        // ---------- Phase 14 — nodes ----------
        builder.Entity<ServerNode>(b =>
        {
            b.Ignore(x => x.UsesKeyAuth);
            b.HasIndex(x => x.Name).IsUnique();
        });

        builder.Entity<ServerService>(b =>
        {
            b.HasOne(x => x.Node).WithMany(n => n.Services).HasForeignKey(x => x.NodeId).OnDelete(DeleteBehavior.Cascade);
            b.Ignore(x => x.UnitName);
            b.HasIndex(x => new { x.NodeId, x.ServiceType }).IsUnique();
        });

        builder.Entity<ServerMetric>(b =>
        {
            b.HasOne(x => x.Node).WithMany().HasForeignKey(x => x.NodeId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.NodeId, x.Timestamp });
        });

        builder.Entity<DomainNode>(b =>
        {
            b.HasOne(x => x.Domain).WithMany().HasForeignKey(x => x.DomainId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Node).WithMany().HasForeignKey(x => x.NodeId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.DomainId).IsUnique();
        });

        builder.Entity<DatabaseNode>(b =>
        {
            b.HasOne(x => x.Database).WithMany().HasForeignKey(x => x.DatabaseId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Node).WithMany().HasForeignKey(x => x.NodeId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.DatabaseId).IsUnique();
        });

        builder.Entity<UserNode>(b =>
        {
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Node).WithMany().HasForeignKey(x => x.NodeId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.UserId).IsUnique();
        });

        builder.Entity<NodeAlert>(b =>
        {
            b.HasOne(x => x.Node).WithMany().HasForeignKey(x => x.NodeId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.NodeId, x.CreatedAt });
            b.HasIndex(x => x.IsAcknowledged);
        });

        // ---------- Phase 12 — VPS (Proxmox) ----------
        builder.Entity<ProxmoxNode>(b =>
        {
            b.Ignore(x => x.UsesToken);
            b.Ignore(x => x.ApiBase);
            b.HasIndex(x => x.Name).IsUnique();
        });

        builder.Entity<VpsTemplate>(b =>
        {
            b.HasOne(x => x.Node).WithMany(n => n.Templates).HasForeignKey(x => x.NodeId).OnDelete(DeleteBehavior.Cascade);
            b.Ignore(x => x.IconClass);
        });

        builder.Entity<VpsInstance>(b =>
        {
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Node).WithMany().HasForeignKey(x => x.NodeId).OnDelete(DeleteBehavior.Restrict);
            b.Ignore(x => x.BandwidthPercent);
            b.Ignore(x => x.IsAlive);
            b.HasIndex(x => x.UserId);
            b.HasIndex(x => x.Status);
        });

        builder.Entity<VpsAction>(b =>
        {
            b.HasOne(x => x.VpsInstance).WithMany().HasForeignKey(x => x.VpsInstanceId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.VpsInstanceId, x.StartedAt });
        });

        builder.Entity<VpsBackup>(b =>
            b.HasOne(x => x.VpsInstance).WithMany().HasForeignKey(x => x.VpsInstanceId).OnDelete(DeleteBehavior.Cascade));

        builder.Entity<VpsSnapshot>(b =>
            b.HasOne(x => x.VpsInstance).WithMany().HasForeignKey(x => x.VpsInstanceId).OnDelete(DeleteBehavior.Cascade));

        builder.Entity<VpsConsoleSession>(b =>
        {
            b.HasOne(x => x.VpsInstance).WithMany().HasForeignKey(x => x.VpsInstanceId).OnDelete(DeleteBehavior.Cascade);
            b.Ignore(x => x.IsValid);
            b.HasIndex(x => x.Token);
        });

        builder.Entity<VpsMetric>(b =>
        {
            b.HasOne(x => x.VpsInstance).WithMany().HasForeignKey(x => x.VpsInstanceId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.VpsInstanceId, x.Timestamp });
        });

        builder.Entity<VpsIpAddress>(b =>
        {
            b.HasOne(x => x.Node).WithMany().HasForeignKey(x => x.NodeId).OnDelete(DeleteBehavior.Cascade);
            b.Ignore(x => x.IsAvailable);
            b.HasIndex(x => x.Address).IsUnique();
        });

        builder.Entity<VpsFirewallRule>(b =>
            b.HasOne(x => x.VpsInstance).WithMany(v => v.FirewallRules).HasForeignKey(x => x.VpsInstanceId).OnDelete(DeleteBehavior.Cascade));

        // ---------- Phase 15 — email server ----------
        builder.Entity<EmailQueue>(b =>
        {
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Domain).WithMany().HasForeignKey(x => x.DomainId).OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(x => new { x.UserId, x.Status });
            b.HasIndex(x => x.CreatedAt);
        });

        builder.Entity<EmailQueueStats>(b =>
        {
            b.HasOne(x => x.Domain).WithMany().HasForeignKey(x => x.DomainId).OnDelete(DeleteBehavior.Cascade);
            b.Ignore(x => x.Total);
            b.Ignore(x => x.DeliveryRate);
            b.HasIndex(x => new { x.DomainId, x.Date });
        });

        builder.Entity<BlacklistEntry>(b =>
        {
            b.HasOne(x => x.Domain).WithMany().HasForeignKey(x => x.DomainId).OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(x => new { x.Value, x.BlacklistName });
        });

        builder.Entity<BlacklistCheck>(b =>
        {
            b.HasOne(x => x.Domain).WithMany().HasForeignKey(x => x.DomainId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.DomainId, x.CheckedAt });
        });

        builder.Entity<EmailBounce>(b =>
        {
            b.HasOne(x => x.Domain).WithMany().HasForeignKey(x => x.DomainId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.DomainId, x.OccurredAt });
        });

        builder.Entity<EmailLog>(b =>
        {
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Domain).WithMany().HasForeignKey(x => x.DomainId).OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(x => new { x.UserId, x.CreatedAt });
            b.HasIndex(x => x.MessageId);
        });

        builder.Entity<MailServerConfig>(b =>
        {
            b.HasOne(x => x.Domain).WithMany().HasForeignKey(x => x.DomainId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.DomainId).IsUnique();
        });

        // ---------- Phase 16 — app hosting ----------
        builder.Entity<AppRuntime>(b =>
        {
            b.Ignore(x => x.Icon);
            b.HasIndex(x => new { x.Type, x.Version }).IsUnique();
        });

        builder.Entity<HostedApp>(b =>
        {
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Domain).WithMany().HasForeignKey(x => x.DomainId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.Runtime).WithMany().HasForeignKey(x => x.RuntimeId).OnDelete(DeleteBehavior.SetNull);
            b.Ignore(x => x.TypeIcon);
            b.Ignore(x => x.IsNode);
            b.Ignore(x => x.IsPython);
            b.HasIndex(x => x.UserId);
            b.HasIndex(x => x.Port);
            b.HasIndex(x => x.Status);
        });

        builder.Entity<HostedAppLog>(b =>
        {
            b.HasOne(x => x.HostedApp).WithMany().HasForeignKey(x => x.HostedAppId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.HostedAppId, x.Timestamp });
        });

        builder.Entity<HostedAppMetric>(b =>
        {
            b.HasOne(x => x.HostedApp).WithMany().HasForeignKey(x => x.HostedAppId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.HostedAppId, x.Timestamp });
        });

        builder.Entity<HostedAppEnv>(b =>
        {
            b.HasOne(x => x.HostedApp).WithMany(a => a.EnvVars).HasForeignKey(x => x.HostedAppId).OnDelete(DeleteBehavior.Cascade);
            b.Ignore(x => x.Masked);
            b.HasIndex(x => new { x.HostedAppId, x.Key }).IsUnique();
        });

        builder.Entity<HostedAppDeploy>(b =>
        {
            b.HasOne(x => x.HostedApp).WithMany().HasForeignKey(x => x.HostedAppId).OnDelete(DeleteBehavior.Cascade);
            b.Ignore(x => x.DurationSeconds);
            b.HasIndex(x => new { x.HostedAppId, x.CreatedAt });
        });

        builder.Entity<HostedAppHealthIncident>(b =>
        {
            b.HasOne(x => x.HostedApp).WithMany().HasForeignKey(x => x.HostedAppId).OnDelete(DeleteBehavior.Cascade);
            b.Ignore(x => x.DurationSeconds);
            b.Ignore(x => x.Ongoing);
            b.HasIndex(x => new { x.HostedAppId, x.StartedAt });
        });

        // ---------- Phase 13 — Cloudflare ----------
        builder.Entity<CloudflareAccount>(b =>
        {
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.Ignore(x => x.MaskedToken);
            b.HasIndex(x => x.UserId);
        });

        builder.Entity<CloudflareDomain>(b =>
        {
            b.HasOne(x => x.Domain).WithMany().HasForeignKey(x => x.DomainId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Account).WithMany(a => a.Domains).HasForeignKey(x => x.CloudflareAccountId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.DomainId).IsUnique();
            b.HasIndex(x => x.ZoneId);
        });

        builder.Entity<CloudflareRule>(b =>
        {
            b.HasOne(x => x.CloudflareDomain).WithMany(d => d.Rules).HasForeignKey(x => x.CloudflareDomainId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.CloudflareDomainId, x.Type });
        });

        builder.Entity<CloudflareAnalytics>(b =>
        {
            b.HasOne(x => x.CloudflareDomain).WithMany().HasForeignKey(x => x.CloudflareDomainId).OnDelete(DeleteBehavior.Cascade);
            b.Ignore(x => x.CacheHitRate);
            b.HasIndex(x => new { x.CloudflareDomainId, x.Date }).IsUnique();
        });

        builder.Entity<CloudflareCache>(b =>
        {
            b.HasOne(x => x.CloudflareDomain).WithMany().HasForeignKey(x => x.CloudflareDomainId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.CloudflareDomainId, x.LastPurgedAt });
        });

        builder.Entity<CloudflareTunnel>(b =>
        {
            b.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.CloudflareAccountId).OnDelete(DeleteBehavior.Cascade);
            b.Ignore(x => x.HostnameList);
            b.HasIndex(x => x.UserId);
        });

        // ---------- Phase 11 — developer tools ----------
        builder.Entity<CronJob>(b =>
        {
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.Ignore(x => x.State);
            b.HasIndex(x => new { x.UserId, x.IsActive });
            b.HasIndex(x => x.NextRunAt);
        });

        builder.Entity<CronJobLog>(b =>
        {
            b.HasOne(x => x.CronJob).WithMany().HasForeignKey(x => x.CronJobId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.CronJobId, x.StartedAt });
        });

        builder.Entity<SshKey>(b =>
        {
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.UserId, x.Fingerprint }).IsUnique();
        });

        builder.Entity<SshAccess>(b =>
        {
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.UserId).IsUnique();
        });

        builder.Entity<SshAccessLog>(b => b.HasIndex(x => new { x.UserId, x.ConnectedAt }));

        builder.Entity<GitRepository>(b =>
        {
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Domain).WithMany().HasForeignKey(x => x.DomainId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.SshKey).WithMany().HasForeignKey(x => x.SshKeyId).OnDelete(DeleteBehavior.SetNull);
            b.Ignore(x => x.PostDeployList);
            b.Ignore(x => x.ShortName);
            b.HasIndex(x => x.UserId);
        });

        builder.Entity<GitDeployment>(b =>
        {
            b.HasOne(x => x.Repository).WithMany().HasForeignKey(x => x.RepositoryId).OnDelete(DeleteBehavior.Cascade);
            b.Ignore(x => x.Duration);
            b.HasIndex(x => new { x.RepositoryId, x.StartedAt });
        });

        builder.Entity<TerminalSession>(b =>
        {
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.Ignore(x => x.Duration);
            b.HasIndex(x => new { x.UserId, x.IsActive });
        });

        builder.Entity<PhpConfig>(b =>
        {
            b.HasOne(x => x.Domain).WithMany().HasForeignKey(x => x.DomainId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.DomainId).IsUnique();
        });

        builder.Entity<StagingSite>(b =>
        {
            b.HasOne(x => x.Domain).WithMany().HasForeignKey(x => x.DomainId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.DomainId).IsUnique();
            b.HasIndex(x => x.ExpiresAt);
        });

        builder.Entity<DeveloperSettings>(b =>
        {
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.UserId).IsUnique();
        });

        builder.Entity<WebhookDelivery>(b =>
        {
            b.HasOne(x => x.Endpoint).WithMany().HasForeignKey(x => x.WebhookEndpointId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.WebhookEndpointId, x.CreatedAt });
        });
    }
}
