using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Models;

namespace SRXPanel.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var db = services.GetRequiredService<ApplicationDbContext>();

        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        if (!db.Packages.Any())
        {
            db.Packages.AddRange(
                new Package
                {
                    Name = "Starter",
                    DiskQuotaMB = 1024,
                    BandwidthQuotaMB = 10240,
                    MaxDomains = 10,
                    MaxEmails = 10,
                    MaxDatabases = 5,
                    MaxFtpAccounts = 5,
                    MaxBackups = 1,
                    Price = 4.99m
                },
                new Package
                {
                    Name = "Professional",
                    DiskQuotaMB = 10240,
                    BandwidthQuotaMB = 102400,
                    MaxDomains = 0,
                    MaxEmails = 0,
                    MaxDatabases = 0,
                    MaxFtpAccounts = 0,
                    MaxBackups = 7,
                    Price = 14.99m
                },
                new Package
                {
                    Name = "Business",
                    DiskQuotaMB = 51200,
                    BandwidthQuotaMB = 512000,
                    MaxDomains = 0,
                    MaxEmails = 0,
                    MaxDatabases = 0,
                    MaxFtpAccounts = 0,
                    MaxBackups = 30,
                    Price = 29.99m
                });

            await db.SaveChangesAsync();
        }

        // Phase 4 — billing plans (public pricing)
        if (!db.Plans.Any())
        {
            db.Plans.AddRange(
                new Plan
                {
                    Name = "Starter", Description = "Perfect for a personal site or blog.",
                    Price = 4.99m, Currency = "usd", BillingCycle = BillingCycle.Monthly,
                    DiskQuotaMB = 1024, BandwidthQuotaMB = 10240,
                    MaxDomains = 1, MaxEmails = 5, MaxDatabases = 2, MaxFtpAccounts = 2, IsActive = true
                },
                new Plan
                {
                    Name = "Business", Description = "For growing sites and small businesses.",
                    Price = 12.99m, Currency = "usd", BillingCycle = BillingCycle.Monthly,
                    DiskQuotaMB = 10240, BandwidthQuotaMB = 102400,
                    MaxDomains = 10, MaxEmails = 50, MaxDatabases = 10, MaxFtpAccounts = 10, IsActive = true
                },
                new Plan
                {
                    Name = "Enterprise", Description = "Unlimited resources for agencies and resellers.",
                    Price = 29.99m, Currency = "usd", BillingCycle = BillingCycle.Monthly,
                    DiskQuotaMB = 51200, BandwidthQuotaMB = 512000,
                    MaxDomains = 0, MaxEmails = 0, MaxDatabases = 0, MaxFtpAccounts = 0, IsActive = true
                });
            await db.SaveChangesAsync();
        }

        if (!db.Coupons.Any())
        {
            db.Coupons.Add(new Coupon
            {
                Code = "WELCOME20", DiscountPercent = 20, MaxUses = 0,
                IsActive = true, CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var adminEmail = "admin@srxpanel.local";
        var adminUser = await userManager.FindByNameAsync("admin");
        if (adminUser == null)
        {
            adminUser = new ApplicationUser
            {
                UserName = "admin",
                Email = adminEmail,
                FullName = "System Administrator",
                EmailConfirmed = true,
                IsActive = true,
                DiskQuotaMB = 0,
                BandwidthQuotaMB = 0,
                CreatedAt = DateTime.UtcNow
            };

            var result = await userManager.CreateAsync(adminUser, "Admin@123456!");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, Roles.SuperAdmin);
            }
        }

        // Phase 6A — demo reseller with allocation, branding, a package and a client.
        var resellerUser = await userManager.FindByNameAsync("reseller1");
        if (resellerUser == null)
        {
            resellerUser = new ApplicationUser
            {
                UserName = "reseller1",
                Email = "reseller1@srxpanel.local",
                FullName = "MilaHost Reseller",
                EmailConfirmed = true,
                IsActive = true,
                DiskQuotaMB = 0,
                BandwidthQuotaMB = 0,
                CreatedAt = DateTime.UtcNow
            };
            var res = await userManager.CreateAsync(resellerUser, "Reseller@123456!");
            if (res.Succeeded)
            {
                await userManager.AddToRoleAsync(resellerUser, Roles.Reseller);
            }
        }

        if (!db.ResellerProfiles.Any(p => p.UserId == resellerUser.Id))
        {
            db.ResellerProfiles.Add(new ResellerProfile
            {
                UserId = resellerUser.Id,
                CompanyName = "MilaHost",
                DiskQuotaMB = 20480,
                BandwidthQuotaMB = 204800,
                MaxClients = 25,
                MaxDomains = 100,
                AllowEmail = true,
                AllowDns = true,
                AllowBackups = true,
                AllowCustomPhp = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            db.ResellerBrandings.Add(new ResellerBranding
            {
                ResellerId = resellerUser.Id,
                PanelTitle = "MilaHost Control Panel",
                PrimaryColor = "#10b981",
                SecondaryColor = "#111827",
                AccentColor = "#34d399",
                FooterText = "Powered by MilaHost",
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        ResellerPackage? demoPackage = db.ResellerPackages.FirstOrDefault(p => p.ResellerId == resellerUser.Id);
        if (demoPackage == null)
        {
            demoPackage = new ResellerPackage
            {
                ResellerId = resellerUser.Id,
                Name = "MilaHost Basic",
                Description = "Entry-level shared hosting.",
                DiskQuotaMB = 2048,
                BandwidthQuotaMB = 20480,
                MaxDomains = 3,
                MaxEmails = 10,
                MaxDatabases = 5,
                MaxFtpAccounts = 3,
                Price = 3.99m,
                BillingCycle = BillingCycle.Monthly,
                IsPublic = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            db.ResellerPackages.Add(demoPackage);
            await db.SaveChangesAsync();
        }

        if (await userManager.FindByNameAsync("client1") == null)
        {
            var clientUser = new ApplicationUser
            {
                UserName = "client1",
                Email = "client1@milahost.local",
                FullName = "MilaHost Demo Client",
                EmailConfirmed = true,
                IsActive = true,
                ResellerId = resellerUser.Id,
                ResellerPackageId = demoPackage.Id,
                DiskQuotaMB = demoPackage.DiskQuotaMB,
                BandwidthQuotaMB = demoPackage.BandwidthQuotaMB,
                CreatedAt = DateTime.UtcNow
            };
            var cr = await userManager.CreateAsync(clientUser, "Client@123456!");
            if (cr.Succeeded)
            {
                await userManager.AddToRoleAsync(clientUser, Roles.Client);
            }
        }

        // Phase 6B — platform settings, currencies, exchange rates, reseller billing seed.
        if (!db.PlatformSettings.Any())
        {
            db.PlatformSettings.Add(new PlatformSettings
            {
                Id = 1,
                PlatformName = "SRXPanel",
                DefaultCurrency = "usd",
                PlatformFeePercent = 10m,
                TrialPeriodDays = 14,
                MinPayoutAmount = 50m,
                DefaultAffiliateCommission = 20m,
                Registration = RegistrationMode.Open,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!db.Currencies.Any())
        {
            db.Currencies.AddRange(
                new Currency { Code = "usd", Name = "US Dollar", Symbol = "$", IsEnabled = true },
                new Currency { Code = "eur", Name = "Euro", Symbol = "€", IsEnabled = true },
                new Currency { Code = "gbp", Name = "British Pound", Symbol = "£", IsEnabled = true },
                new Currency { Code = "azn", Name = "Azerbaijani Manat", Symbol = "₼", IsEnabled = true },
                new Currency { Code = "try", Name = "Turkish Lira", Symbol = "₺", IsEnabled = false });
        }

        if (!db.ExchangeRates.Any())
        {
            db.ExchangeRates.AddRange(
                new ExchangeRate { FromCurrency = "usd", ToCurrency = "eur", Rate = 0.92m, UpdatedAt = DateTime.UtcNow },
                new ExchangeRate { FromCurrency = "usd", ToCurrency = "gbp", Rate = 0.79m, UpdatedAt = DateTime.UtcNow },
                new ExchangeRate { FromCurrency = "usd", ToCurrency = "azn", Rate = 1.70m, UpdatedAt = DateTime.UtcNow },
                new ExchangeRate { FromCurrency = "usd", ToCurrency = "try", Rate = 32.0m, UpdatedAt = DateTime.UtcNow });
        }
        await db.SaveChangesAsync();

        if (!db.ResellerBillingConfigs.Any(c => c.ResellerId == resellerUser.Id))
        {
            db.ResellerBillingConfigs.Add(new ResellerBillingConfig
            {
                ResellerId = resellerUser.Id,
                Model = ResellerBillingModel.Prepaid,
                PlatformFeePercent = 10m,
                MinPayoutAmount = 50m,
                Currency = "usd",
                CreatedAt = DateTime.UtcNow
            });
            db.ResellerTransactions.Add(new ResellerTransaction
            {
                ResellerId = resellerUser.Id,
                Type = ResellerTransactionType.Credit,
                Amount = 100m,
                Balance = 100m,
                Description = "Welcome credit",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await SeedFrontendAsync(db, adminUser);
        await SeedSecurityAsync(db);
        await SeedAppsAsync(db);
        await SeedDeveloperAsync(db);
        await SeedNodesAsync(db);
        await SeedVpsAsync(db);
        await SeedEmailServerAsync(db);
        await SeedAppHostingAsync(db);
    }

    // ---------------- Phase 16 — app hosting ----------------
    private static async Task SeedAppHostingAsync(ApplicationDbContext db)
    {
        if (db.AppRuntimes.Any()) return;

        AppRuntime Rt(string name, AppRuntimeType type, string version, string path, bool def) =>
            new() { Name = name, Type = type, Version = version, BinaryPath = path, IsActive = true, IsDefault = def };

        var node = Rt("Node.js 20", AppRuntimeType.NodeJs, "20.15.0", "/root/.nvm/versions/node/v20.15.0/bin/node", true);
        db.AppRuntimes.AddRange(
            node,
            Rt("Node.js 18", AppRuntimeType.NodeJs, "18.20.3", "/root/.nvm/versions/node/v18.20.3/bin/node", false),
            Rt("Python 3.11", AppRuntimeType.Python, "3.11.9", "/root/.pyenv/versions/3.11.9/bin/python", true),
            Rt("Ruby 3.2", AppRuntimeType.Ruby, "3.2.4", "/root/.rbenv/versions/3.2.4/bin/ruby", true),
            Rt("Go 1.21", AppRuntimeType.Go, "1.21.11", "/usr/local/go/1.21.11/bin/go", true));
        await db.SaveChangesAsync();

        // A demo running Node app — prefer client1 so it shows in the standard demo flow.
        var client1 = await db.Users.FirstOrDefaultAsync(u => u.Email == "client1@milahost.local");
        var domain = client1 != null
            ? await db.Domains.Where(d => d.UserId == client1.Id).OrderBy(d => d.Id).FirstOrDefaultAsync()
            : null;
        domain ??= await db.Domains.OrderBy(d => d.Id).FirstOrDefaultAsync();
        if (domain == null) return;

        var app = new HostedApp
        {
            UserId = domain.UserId, DomainId = domain.Id, Name = "demo-api", Type = AppRuntimeType.NodeJs,
            RuntimeId = node.Id, AppPath = "/home/user/apps/demo-api", EntryPoint = "app.js",
            StartCommand = "node app.js", Port = 3000, ProcessCount = 2, ClusterMode = true,
            Status = HostedAppStatus.Running, Pm2Id = 1, Pid = 4821, CpuPercent = 3.4, MemoryMB = 128,
            RestartCount = 1, AutoRestart = true, MaxMemoryRestartMB = 256,
            CreatedAt = DateTime.UtcNow.AddDays(-5), StartedAt = DateTime.UtcNow.AddHours(-30),
            LastHealthCheckAt = DateTime.UtcNow, Healthy = true, Uptime = 30 * 3600
        };
        db.HostedApps.Add(app);
        await db.SaveChangesAsync();

        db.HostedAppEnvs.AddRange(
            new HostedAppEnv { HostedAppId = app.Id, Key = "NODE_ENV", Value = "production" },
            new HostedAppEnv { HostedAppId = app.Id, Key = "DATABASE_URL", Value = "postgres://localhost/demo", IsSecret = true },
            new HostedAppEnv { HostedAppId = app.Id, Key = "API_KEY", Value = "sk_live_demo_1234567890", IsSecret = true });

        // A little metric history + a successful deploy.
        var rnd = new Random(16);
        for (var i = 60; i >= 0; i--)
            db.HostedAppMetrics.Add(new HostedAppMetric
            {
                HostedAppId = app.Id, Timestamp = DateTime.UtcNow.AddMinutes(-i),
                CpuPercent = Math.Round(1 + rnd.NextDouble() * 7, 1), MemoryMB = Math.Round(90 + rnd.NextDouble() * 80, 1),
                RequestsPerSec = Math.Round(rnd.NextDouble() * 40, 1), ResponseTimeMs = Math.Round(5 + rnd.NextDouble() * 50, 1)
            });
        db.HostedAppDeploys.Add(new HostedAppDeploy
        {
            HostedAppId = app.Id, Type = AppDeployType.Git, Status = AppDeployStatus.Success,
            CommitHash = "a1b2c3d", Output = "npm install\nadded 214 packages\n✓ deployed",
            CreatedAt = DateTime.UtcNow.AddDays(-1), CompletedAt = DateTime.UtcNow.AddDays(-1).AddSeconds(42)
        });
        await db.SaveChangesAsync();
    }

    // ---------------- Phase 15 — email server ----------------
    private static async Task SeedEmailServerAsync(ApplicationDbContext db)
    {
        if (db.EmailQueues.Any() || db.EmailLogs.Any()) return;

        var domain = await db.Domains.OrderBy(d => d.Id).FirstOrDefaultAsync();
        if (domain == null) return;
        var userId = domain.UserId;
        var mailHost = $"mail.{domain.DomainName}";
        var rnd = new Random(15);

        // Per-domain mail server config.
        db.MailServerConfigs.Add(new MailServerConfig
        {
            DomainId = domain.Id, SmtpHost = mailHost, ImapHost = mailHost, Pop3Host = mailHost,
            BlacklistAutoCheck = true, BlacklistSchedule = "daily", LastBlacklistCheckAt = DateTime.UtcNow.AddHours(-3)
        });

        // A spread of queue items.
        var statuses = new[]
        {
            EmailQueueStatus.Queued, EmailQueueStatus.Queued, EmailQueueStatus.Sending,
            EmailQueueStatus.Deferred, EmailQueueStatus.Failed
        };
        for (var i = 0; i < 12; i++)
        {
            var st = statuses[i % statuses.Length];
            db.EmailQueues.Add(new EmailQueue
            {
                UserId = userId, DomainId = domain.Id,
                FromAddress = $"noreply@{domain.DomainName}", ToAddress = $"user{rnd.Next(1, 500)}@example.com",
                Subject = $"Message #{1000 + i}", Body = "Sample queued message.",
                Status = st, Attempts = st == EmailQueueStatus.Deferred ? 2 : st == EmailQueueStatus.Failed ? 5 : 0,
                ErrorMessage = st == EmailQueueStatus.Failed ? "550 5.1.1 User unknown"
                    : st == EmailQueueStatus.Deferred ? "451 4.7.1 Greylisted" : null,
                CreatedAt = DateTime.UtcNow.AddMinutes(-rnd.Next(1, 240))
            });
        }

        // Delivery log history + daily rollups over the last 14 days.
        for (var day = 13; day >= 0; day--)
        {
            var date = DateTime.UtcNow.Date.AddDays(-day);
            var sent = rnd.Next(40, 160);
            var bounced = rnd.Next(0, 8);
            var deferred = rnd.Next(0, 5);
            var spam = rnd.Next(0, 6);

            db.EmailQueueStats.Add(new EmailQueueStats
            {
                DomainId = domain.Id, Date = date,
                TotalSent = sent, TotalBounced = bounced, TotalDeferred = deferred, TotalSpam = spam,
                TotalFailed = rnd.Next(0, 4)
            });

            // A handful of representative log rows per day.
            for (var k = 0; k < 6; k++)
            {
                var roll = rnd.NextDouble();
                var status = roll < 0.8 ? EmailLogStatus.Delivered : roll < 0.9 ? EmailLogStatus.Spam : EmailLogStatus.Bounced;
                var score = Math.Round(rnd.NextDouble() * 7, 1);
                db.EmailLogs.Add(new EmailLog
                {
                    UserId = userId, DomainId = domain.Id, FromAddress = $"noreply@{domain.DomainName}",
                    ToAddress = $"contact{rnd.Next(1, 900)}@example.net", Subject = $"Newsletter {date:MMM dd} #{k}",
                    MessageId = $"<{Guid.NewGuid():N}@srxpanel>", Status = status, SpamScore = score,
                    DeliveredAt = status == EmailLogStatus.Delivered ? date.AddHours(rnd.Next(0, 23)) : null,
                    CreatedAt = date.AddHours(rnd.Next(0, 23))
                });
            }
        }

        // A blacklist check (clean) + a couple of bounces.
        db.BlacklistChecks.Add(new BlacklistCheck
        {
            DomainId = domain.Id, UserId = userId, CheckType = BlacklistCheckType.Domain,
            Value = domain.DomainName, Status = BlacklistCheckStatus.Clean, CheckedAt = DateTime.UtcNow.AddHours(-3)
        });
        db.EmailBounces.AddRange(
            new EmailBounce { DomainId = domain.Id, EmailAddress = "old-user@dead.example", BounceType = BounceType.Hard, BounceReason = "550 5.1.1 User unknown", OccurredAt = DateTime.UtcNow.AddDays(-1) },
            new EmailBounce { DomainId = domain.Id, EmailAddress = "full-inbox@example.org", BounceType = BounceType.Soft, BounceReason = "452 4.2.2 Mailbox full", OccurredAt = DateTime.UtcNow.AddHours(-6) });

        await db.SaveChangesAsync();
    }

    // ---------------- Phase 12 — VPS / Proxmox ----------------
    private static async Task SeedVpsAsync(ApplicationDbContext db)
    {
        if (db.ProxmoxNodes.Any()) return;

        var pveFra = new ProxmoxNode
        {
            Name = "pve-fra-01", Host = "10.0.20.11", Port = 8006, Username = "root@pam",
            TokenId = "root@pam!srxpanel", TokenSecret = "sim_token", VerifySsl = false,
            MaxVms = 120, Storage = "local-lvm", Network = "vmbr0", Location = "Frankfurt, DE",
            LastSeenAt = DateTime.UtcNow
        };
        var pveNyc = new ProxmoxNode
        {
            Name = "pve-nyc-01", Host = "10.0.30.11", Port = 8006, Username = "root@pam",
            TokenId = "root@pam!srxpanel", TokenSecret = "sim_token", VerifySsl = false,
            MaxVms = 80, Storage = "local-lvm", Network = "vmbr0", Location = "New York, US",
            LastSeenAt = DateTime.UtcNow
        };
        db.ProxmoxNodes.AddRange(pveFra, pveNyc);
        await db.SaveChangesAsync();

        VpsTemplate Tpl(ProxmoxNode node, string name, string os, int vmid, int disk, int ram, int cpu) =>
            new() { NodeId = node.Id, Name = name, OsType = os, TemplateId = vmid, MinDiskGB = disk, MinRamMB = ram, MinCpuCores = cpu, IsActive = true };

        foreach (var node in new[] { pveFra, pveNyc })
            db.VpsTemplates.AddRange(
                Tpl(node, "Ubuntu 24.04 LTS", "ubuntu", 9000, 10, 512, 1),
                Tpl(node, "Ubuntu 22.04 LTS", "ubuntu", 9001, 10, 512, 1),
                Tpl(node, "Debian 12", "debian", 9010, 8, 512, 1),
                Tpl(node, "Rocky Linux 9", "rocky", 9020, 12, 768, 1));

        // IP pool per node.
        for (var i = 10; i < 26; i++)
            db.VpsIpAddresses.Add(new VpsIpAddress { NodeId = pveFra.Id, Address = $"203.0.113.{i}", Gateway = "203.0.113.1", Prefix = 24 });
        for (var i = 10; i < 26; i++)
            db.VpsIpAddresses.Add(new VpsIpAddress { NodeId = pveNyc.Id, Address = $"198.51.100.{i}", Gateway = "198.51.100.1", Prefix = 24 });

        // Point the Frankfurt VPS plans at the Frankfurt node, the US one at New York.
        var plans = await db.VpsPlans.ToListAsync();
        foreach (var plan in plans)
            plan.NodeId = plan.Location.Contains("New York") ? pveNyc.Id : pveFra.Id;

        await db.SaveChangesAsync();

        // A demo running VPS for client1 so the client + admin views have data.
        var client1 = await db.Users.FirstOrDefaultAsync(u => u.Email == "client1@milahost.local");
        var starter = plans.FirstOrDefault(p => p.Name.Contains("Starter"));
        if (client1 != null && starter != null)
        {
            var firstIp = await db.VpsIpAddresses.FirstAsync(a => a.NodeId == pveFra.Id && a.AssignedInstanceId == null);
            var vps = new VpsInstance
            {
                UserId = client1.Id, PlanId = starter.Id, NodeId = pveFra.Id, VmId = 100,
                Hostname = "demo.client1.dev", IpAddress = firstIp.Address, Ipv6Address = "2a01:4f8:100:64::1",
                MacAddress = "BC:24:11:00:64:2C", ReverseDns = "demo.client1.dev",
                Status = VpsStatus.Running, OsTemplate = "ubuntu",
                CpuCores = starter.CpuCores, RamMB = starter.RamMB, DiskGB = starter.DiskGB, BandwidthGB = starter.BandwidthGB,
                BandwidthUsed = 340.5, RootPassword = "Demo@123456!", SshPort = 22,
                CreatedAt = DateTime.UtcNow.AddDays(-9), ExpiresAt = DateTime.UtcNow.AddDays(21),
                BandwidthCycleStart = DateTime.UtcNow.AddDays(-9)
            };
            db.VpsInstances.Add(vps);
            await db.SaveChangesAsync();

            firstIp.AssignedInstanceId = vps.Id;
            db.ClientServices.Add(new ClientService
            {
                UserId = client1.Id, Type = ClientServiceType.Vps, ReferenceId = vps.Id,
                Name = $"{starter.Name} — {vps.Hostname}",
                ResourceSummary = $"{starter.CpuCores} vCPU · {starter.RamMB / 1024} GB RAM · {starter.DiskGB} GB",
                Price = starter.Price, BillingCycle = starter.BillingCycle, Status = SubscriptionStatus.Active,
                CurrentPeriodStart = DateTime.UtcNow.AddDays(-9), CurrentPeriodEnd = DateTime.UtcNow.AddDays(21)
            });
            await db.SaveChangesAsync();
        }
    }

    // ---------------- Phase 14 — server fleet ----------------
    private static async Task SeedNodesAsync(ApplicationDbContext db)
    {
        if (db.ServerNodes.Any()) return;

        ServerNode Node(string name, string host, string ip, NodeType type, int cpu, int ram, int disk, string location, int weight) =>
            new()
            {
                Name = name, Hostname = host, IpAddress = ip, SshPort = 22, SshUsername = "root",
                SshKeyPath = "/root/.ssh/id_rsa", Type = type, Status = NodeStatus.Online,
                CpuCores = cpu, RamGB = ram, DiskGB = disk, Location = location, Weight = weight,
                Os = "Ubuntu 24.04 LTS", IsActive = true, LastPingAt = DateTime.UtcNow, LatencyMs = 12,
                CreatedAt = DateTime.UtcNow
            };

        var web1 = Node("web-fra-01", "web1.srxpanel.net", "203.0.113.11", NodeType.Primary, 8, 32, 500, "Frankfurt, DE", 100);
        var web2 = Node("web-nyc-01", "web2.srxpanel.net", "198.51.100.21", NodeType.Secondary, 4, 16, 250, "New York, US", 80);
        var mail1 = Node("mail-fra-01", "mail.srxpanel.net", "203.0.113.25", NodeType.Mail, 2, 8, 200, "Frankfurt, DE", 100);
        var dns1 = Node("ns1-fra-01", "ns1.srxpanel.net", "203.0.113.53", NodeType.Dns, 2, 4, 80, "Frankfurt, DE", 100);
        var storage1 = Node("store-fra-01", "storage.srxpanel.net", "203.0.113.90", NodeType.Storage, 4, 16, 4000, "Frankfurt, DE", 100);

        db.ServerNodes.AddRange(web1, web2, mail1, dns1, storage1);
        await db.SaveChangesAsync();

        ServerService Svc(int nodeId, ServerServiceType type, string version, int port) =>
            new() { NodeId = nodeId, ServiceType = type, Status = ServerServiceStatus.Running, Version = version, Port = port, LastCheckedAt = DateTime.UtcNow };

        db.ServerServices.AddRange(
            // web nodes
            Svc(web1.Id, ServerServiceType.Nginx, "1.24.0", 80), Svc(web1.Id, ServerServiceType.MySQL, "8.0.39", 3306),
            Svc(web1.Id, ServerServiceType.PHP, "8.3", 9000), Svc(web1.Id, ServerServiceType.FTP, "3.0.5", 21),
            Svc(web2.Id, ServerServiceType.Nginx, "1.24.0", 80), Svc(web2.Id, ServerServiceType.MySQL, "8.0.39", 3306),
            Svc(web2.Id, ServerServiceType.PHP, "8.3", 9000),
            // mail node
            Svc(mail1.Id, ServerServiceType.Email, "3.8.6", 25),
            // dns node
            Svc(dns1.Id, ServerServiceType.DNS, "9.18", 53),
            // storage node
            Svc(storage1.Id, ServerServiceType.Backup, "1.0", 873));
        await db.SaveChangesAsync();

        // Place the seeded demo domains on the primary web node.
        var domains = db.Domains.ToList();
        foreach (var domain in domains)
            db.DomainNodes.Add(new DomainNode { DomainId = domain.Id, NodeId = web1.Id, IsPrimary = true, AssignedAt = DateTime.UtcNow });

        // A representative alert so the Alerts page has content on a fresh install.
        db.NodeAlerts.Add(new NodeAlert
        {
            NodeId = web2.Id, Type = NodeAlertType.RamHigh, Severity = AlertSeverity.Warning,
            Message = "web-nyc-01 RAM is at 87% (threshold 85%).", DedupeKey = $"{web2.Id}:RamHigh",
            CreatedAt = DateTime.UtcNow.AddMinutes(-8)
        });

        await db.SaveChangesAsync();
    }

    // ---------------- Phase 11 — developer tools ----------------
    private static async Task SeedDeveloperAsync(ApplicationDbContext db)
    {
        var client = db.Users.FirstOrDefault(u => u.UserName == "client1");
        if (client == null || db.CronJobs.Any(j => j.UserId == client.Id)) return;

        // Two representative cron jobs, so the manager, the scheduler and the execution
        // log all have something real to show on a fresh install.
        static DateTime? NextRun(string expression) =>
            SRXPanel.Services.Developer.CronExpression.TryParse(expression, out var schedule, out _)
                ? schedule!.Next(DateTime.UtcNow)
                : null;

        db.CronJobs.AddRange(
            new CronJob
            {
                UserId = client.Id,
                Command = "php public_html/artisan schedule:run",
                Schedule = "* * * * *",
                Description = "Laravel scheduler",
                Email = client.Email,
                EmailOnFailure = true,
                IsActive = true,
                NextRunAt = NextRun("* * * * *"),
                CreatedAt = DateTime.UtcNow
            },
            new CronJob
            {
                UserId = client.Id,
                Command = "php public_html/cron/cleanup.php --days=30",
                Schedule = "0 3 * * *",
                Description = "Nightly cleanup",
                Email = client.Email,
                EmailOnFailure = true,
                IsActive = true,
                NextRunAt = NextRun("0 3 * * *"),
                CreatedAt = DateTime.UtcNow
            });

        db.SshAccesses.Add(new SshAccess { UserId = client.Id, IsEnabled = false, Port = 22 });
        await db.SaveChangesAsync();
    }

    // ---------------- Phase 10 — application catalogue ----------------
    private static async Task SeedAppsAsync(ApplicationDbContext db)
    {
        if (!db.AppUpdateSettings.Any())
        {
            db.AppUpdateSettings.Add(new AppUpdateSettings { Id = 1, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        if (db.AppDefinitions.Any()) return;

        AppDefinition App(string name, string slug, AppCategory cat, string version, string icon,
            int disk, string php, string desc, string features, bool db_ = true, double rating = 4.5, int installs = 0) =>
            new()
            {
                Name = name, Slug = slug, Category = cat, Version = version, IconPath = icon,
                MinDiskMB = disk, MinPhpVersion = php, Description = desc, Features = features,
                RequiresDatabase = db_, Rating = rating, InstallCount = installs,
                DownloadUrl = $"https://downloads.srxpanel.com/apps/{slug}-{version}.tar.gz",
                Requirements = $"PHP {php}+ · {disk} MB disk" + (db_ ? " · MySQL 5.7+" : ""),
                Changelog = $"{version} — latest stable release.",
                IsActive = true, CreatedAt = DateTime.UtcNow
            };

        db.AppDefinitions.AddRange(
            // CMS
            App("WordPress", "wordpress", AppCategory.Cms, "6.5.2", "bi-wordpress", 80, "8.1",
                "The world's most popular content management system, powering over 40% of the web.",
                "Thousands of themes and plugins\nBlock-based editor\nSEO friendly\nREST API", rating: 4.8, installs: 1240),
            App("Joomla", "joomla", AppCategory.Cms, "5.1.0", "bi-journal-richtext", 100, "8.1",
                "A flexible, multilingual CMS for building powerful websites and applications.",
                "Multilingual out of the box\nGranular ACL\nExtensible with extensions", rating: 4.2, installs: 210),
            App("Drupal", "drupal", AppCategory.Cms, "10.2.5", "bi-droplet", 120, "8.2",
                "An enterprise-grade CMS trusted by governments and large organisations.",
                "Robust taxonomy\nStrong security track record\nHeadless-ready", rating: 4.3, installs: 165),
            App("TYPO3", "typo3", AppCategory.Cms, "13.0.1", "bi-diagram-3", 150, "8.2",
                "An enterprise CMS popular across Europe for large, structured websites.",
                "Enterprise workflows\nMulti-site management\nStrong caching", rating: 4.0, installs: 42),
            App("Concrete CMS", "concrete-cms", AppCategory.Cms, "9.2.5", "bi-bricks", 110, "8.1",
                "In-context editing CMS that lets you edit pages directly on the page.",
                "In-context editing\nBuilt-in file manager\nBlock library", rating: 4.1, installs: 33),

            // E-Commerce
            App("WooCommerce", "woocommerce", AppCategory.ECommerce, "8.6.1", "bi-cart", 150, "8.1",
                "WordPress plus WooCommerce — the most popular open-source e-commerce stack.",
                "WordPress included\nPayment gateways\nInventory management\nExtensions marketplace", rating: 4.7, installs: 890),
            App("PrestaShop", "prestashop", AppCategory.ECommerce, "8.1.5", "bi-shop", 180, "8.1",
                "A full-featured open-source e-commerce platform with a rich module ecosystem.",
                "Multi-store\nMulti-currency\n5000+ modules", rating: 4.2, installs: 128),
            App("OpenCart", "opencart", AppCategory.ECommerce, "4.0.2.3", "bi-basket", 90, "8.0",
                "A lightweight, easy-to-use shopping cart with a clean admin interface.",
                "Lightweight and fast\nMulti-store\nLarge extension library", rating: 4.0, installs: 96),
            App("Magento Open Source", "magento", AppCategory.ECommerce, "2.4.7", "bi-bag", 900, "8.2",
                "An enterprise e-commerce platform for large catalogues and complex requirements.",
                "Enterprise scalability\nAdvanced merchandising\nB2B features", rating: 3.9, installs: 21),
            App("WP EasyCart", "wp-easycart", AppCategory.ECommerce, "5.5.2", "bi-cart-check", 100, "8.1",
                "A simple WordPress shopping cart for selling products quickly.",
                "Quick setup\nWordPress native\nDigital downloads", rating: 3.8, installs: 18),

            // Frameworks
            App("Laravel", "laravel", AppCategory.Framework, "11.0", "bi-code-slash", 60, "8.2",
                "An elegant PHP framework for building modern web applications. Skeleton install.",
                "Eloquent ORM\nArtisan CLI\nBlade templating\nQueues and jobs", rating: 4.9, installs: 340),
            App("Symfony", "symfony", AppCategory.Framework, "7.0.4", "bi-gear-wide-connected", 70, "8.2",
                "A high-performance PHP framework of reusable components. Skeleton install.",
                "Reusable components\nDoctrine ORM\nStrong DI container", rating: 4.6, installs: 152),
            App("CodeIgniter", "codeigniter", AppCategory.Framework, "4.5.0", "bi-lightning", 30, "8.1",
                "A small-footprint PHP framework with exceptional performance.",
                "Tiny footprint\nMinimal configuration\nFast", rating: 4.1, installs: 74),
            App("CakePHP", "cakephp", AppCategory.Framework, "5.0.7", "bi-egg-fried", 50, "8.1",
                "A rapid-development PHP framework built on convention over configuration.",
                "Scaffolding\nCRUD generation\nSecure by default", rating: 4.0, installs: 38),
            App("Yii2", "yii2", AppCategory.Framework, "2.0.49", "bi-hexagon", 45, "8.0",
                "A fast, secure and efficient PHP framework for large-scale applications.",
                "Gii code generator\nRBAC\nCaching layers", rating: 4.0, installs: 29),

            // Forums / community
            App("phpBB", "phpbb", AppCategory.Forum, "3.3.11", "bi-chat-left-text", 60, "8.0",
                "The most widely used open-source bulletin board software.",
                "Mature and stable\nExtensions\nModeration tools", rating: 4.2, installs: 87),
            App("MyBB", "mybb", AppCategory.Forum, "1.8.38", "bi-chat-dots", 50, "8.0",
                "A free, efficient and powerful forum software with a modern admin panel.",
                "Fast\nPlugin system\nTemplate engine", rating: 4.1, installs: 44),
            App("Discourse", "discourse", AppCategory.Forum, "3.2.1", "bi-chat-square-quote", 800, "8.2",
                "A modern, Docker-based discussion platform for civilised community conversation.",
                "Docker-based deployment\nReal-time updates\nTrust levels", db_: false, rating: 4.6, installs: 26),
            App("Flarum", "flarum", AppCategory.Forum, "1.8.5", "bi-chat-heart", 40, "8.1",
                "A delightfully simple, fast and free forum software.",
                "Single-page app\nExtensible\nBeautiful by default", rating: 4.4, installs: 31),

            // Blogs
            App("Ghost", "ghost", AppCategory.Blog, "5.82.0", "bi-lightbulb", 200, "8.1",
                "A professional publishing platform built on Node.js with a superb editor.",
                "Node.js powered\nMembership + newsletters\nMarkdown editor", db_: false, rating: 4.7, installs: 63),
            App("Grav CMS", "grav", AppCategory.Blog, "1.7.45", "bi-file-earmark-text", 35, "8.0",
                "A modern flat-file CMS — no database required, blazing fast.",
                "Flat-file (no database)\nMarkdown content\nTwig templating", db_: false, rating: 4.5, installs: 41),
            App("Pico CMS", "picocms", AppCategory.Blog, "3.0.0", "bi-feather", 15, "8.0",
                "A stupidly simple, blazing fast flat-file CMS.",
                "No database\nMarkdown only\nTiny footprint", db_: false, rating: 4.2, installs: 17),

            // Wiki
            App("MediaWiki", "mediawiki", AppCategory.Wiki, "1.41.1", "bi-book", 130, "8.1",
                "The wiki engine that powers Wikipedia — battle-tested at massive scale.",
                "Powers Wikipedia\nRich extension ecosystem\nVersion history", rating: 4.3, installs: 52),
            App("DokuWiki", "dokuwiki", AppCategory.Wiki, "2024-02-06", "bi-journal-bookmark", 25, "8.0",
                "A simple, versatile wiki that works on plain text files — no database needed.",
                "No database\nAccess controls\nHundreds of plugins", db_: false, rating: 4.4, installs: 36),
            App("BookStack", "bookstack", AppCategory.Wiki, "24.02.2", "bi-journals", 90, "8.1",
                "A simple, self-hosted platform for organising and storing information.",
                "Books, chapters and pages\nWYSIWYG + Markdown\nFull-text search", rating: 4.6, installs: 48),

            // Project management / productivity
            App("Nextcloud", "nextcloud", AppCategory.ProjectManagement, "28.0.4", "bi-cloud", 400, "8.1",
                "A self-hosted productivity platform: files, calendar, contacts and collaboration.",
                "File sync and share\nCalendar and contacts\nOnline office\nApp store", rating: 4.7, installs: 118),
            App("Phabricator", "phabricator", AppCategory.ProjectManagement, "2024.09", "bi-kanban", 250, "8.0",
                "A suite of open-source tools for peer code review, task management and hosting.",
                "Code review\nTask tracking\nRepository hosting", rating: 3.9, installs: 12),

            // Support
            App("osTicket", "osticket", AppCategory.Support, "1.18.1", "bi-life-preserver", 70, "8.0",
                "A widely used open-source support ticket system.",
                "Ticket routing\nCanned responses\nSLA management", rating: 4.1, installs: 58),
            App("Helpdesk", "helpdesk", AppCategory.Support, "2.4.0", "bi-headset", 60, "8.1",
                "A lightweight self-hosted helpdesk with email piping and knowledge base.",
                "Email piping\nKnowledge base\nAgent roles", rating: 3.9, installs: 14)
        );

        await db.SaveChangesAsync();
    }

    // ---------------- Phase 9 — security demo data ----------------
    private static async Task SeedSecurityAsync(ApplicationDbContext db)
    {
        if (!db.SecuritySettings.Any())
        {
            db.SecuritySettings.Add(new SecuritySettings { Id = 1, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        if (!db.LoginAttempts.Any())
        {
            var rnd = new Random(42);
            var ips = new[] { "203.0.113.7", "45.83.12.9", "185.220.101.4", "91.219.236.15", "103.72.144.2" };
            var countries = new[] { "RU", "CN", "US", "NL", "BR" };
            var attempts = new List<LoginAttempt>();
            for (var i = 0; i < 40; i++)
            {
                var idx = rnd.Next(ips.Length);
                attempts.Add(new LoginAttempt
                {
                    IP = ips[idx], Country = countries[idx], Username = rnd.Next(2) == 0 ? "admin" : "root",
                    Type = (LoginAttemptType)rnd.Next(4), Success = rnd.Next(6) == 0,
                    Timestamp = DateTime.UtcNow.AddMinutes(-rnd.Next(0, 4320)),
                    UserAgent = "Mozilla/5.0 (compatible; scanner)"
                });
            }
            db.LoginAttempts.AddRange(attempts);

            db.BlockedIPs.Add(new BlockedIP
            {
                IP = "185.220.101.4", Reason = "Exceeded 5 failed Panel attempts", Country = "NL",
                BlockedAt = DateTime.UtcNow.AddHours(-2), ExpiresAt = DateTime.UtcNow.AddMinutes(28), IsManual = false
            });

            db.IpAccessRules.AddRange(
                new IpAccessRule { Kind = IpRuleKind.WhitelistIp, Value = "203.0.113.10", Reason = "Office IP", CreatedAt = DateTime.UtcNow },
                new IpAccessRule { Kind = IpRuleKind.BlacklistIp, Value = "45.83.12.9", Reason = "Repeated attacks", CreatedAt = DateTime.UtcNow },
                new IpAccessRule { Kind = IpRuleKind.BlockCountry, Value = "KP", Reason = "Geo policy", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        // Attach a few WAF alerts to the first domain (if any exist).
        if (!db.ModSecurityAlerts.Any())
        {
            var domain = db.Domains.OrderBy(d => d.Id).FirstOrDefault();
            if (domain != null)
            {
                var rnd = new Random(7);
                var rules = new (string Id, string Msg)[]
                {
                    ("942100", "SQL Injection Attack Detected via libinjection"),
                    ("941100", "XSS Attack Detected via libinjection"),
                    ("930100", "Path Traversal Attack (/../)"),
                    ("913100", "Found request associated with a security scanner")
                };
                var uris = new[] { "/?id=1' OR '1'='1", "/search?q=<script>", "/../../etc/passwd", "/wp-login.php" };
                var ips = new[] { "45.83.12.9", "185.220.101.4", "203.0.113.7" };
                for (var i = 0; i < 12; i++)
                {
                    var r = rules[i % rules.Length];
                    db.ModSecurityAlerts.Add(new ModSecurityAlert
                    {
                        DomainId = domain.Id, IP = ips[i % ips.Length], Method = "GET", URI = uris[i % uris.Length],
                        RuleId = r.Id, RuleMessage = r.Msg, Action = i % 3 == 0 ? "logged" : "blocked",
                        Timestamp = DateTime.UtcNow.AddMinutes(-rnd.Next(0, 2880))
                    });
                }
                db.WafConfigs.Add(new WafConfig { DomainId = domain.Id, Enabled = true, Mode = WafMode.Prevention, UpdatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
        }
    }

    // ---------------- Phase 8 — public frontend content ----------------
    private static async Task SeedFrontendAsync(ApplicationDbContext db, ApplicationUser? admin)
    {
        var frontend = db.FrontendSettings.FirstOrDefault(s => s.Id == 1);
        if (frontend == null)
        {
            // Model defaults are already generic / white-labelled.
            db.FrontendSettings.Add(new FrontendSettings { Id = 1, UpdatedAt = DateTime.UtcNow });
        }
        else if (frontend.SiteName == "SRXPanel"
                 || (frontend.HeroSubheadline?.Contains("cPanel") ?? false)
                 || (frontend.SocialGithub?.Contains("srxpanel") ?? false))
        {
            // Upgrade a legacy (never-customised) row to the white-labelled defaults.
            var d = new FrontendSettings();
            frontend.SiteName = d.SiteName;
            frontend.Tagline = d.Tagline;
            frontend.HeroHeadline = d.HeroHeadline;
            frontend.HeroSubheadline = d.HeroSubheadline;
            frontend.MetaDescription = d.MetaDescription;
            frontend.AboutContent = d.AboutContent;
            frontend.ContactEmail = d.ContactEmail;
            frontend.SocialGithub = null;
            frontend.UpdatedAt = DateTime.UtcNow;
        }

        if (!db.FeatureItems.Any())
        {
            db.FeatureItems.AddRange(
                new FeatureItem { Icon = "bi-hdd", Title = "SSD Storage", Description = "Blazing-fast NVMe SSD storage on every plan.", SortOrder = 1 },
                new FeatureItem { Icon = "bi-shield-lock", Title = "Free SSL", Description = "Automatic Let's Encrypt certificates with auto-renewal.", SortOrder = 2 },
                new FeatureItem { Icon = "bi-envelope-at", Title = "Email Hosting", Description = "Professional mailboxes, forwarders and autoresponders.", SortOrder = 3 },
                new FeatureItem { Icon = "bi-hdd-stack", Title = "Daily Backups", Description = "Automated daily backups you can restore in one click.", SortOrder = 4 },
                new FeatureItem { Icon = "bi-lightning-charge", Title = "1-Click Install", Description = "Install WordPress and 300+ apps in seconds.", SortOrder = 5 },
                new FeatureItem { Icon = "bi-headset", Title = "24/7 Support", Description = "Real humans ready to help, any time of day.", SortOrder = 6 });
        }

        if (!db.StatCounters.Any())
        {
            db.StatCounters.AddRange(
                new StatCounter { Label = "Happy Clients", Value = 12500, Suffix = "+", Icon = "bi-people", SortOrder = 1 },
                new StatCounter { Label = "Uptime", Value = 99, Suffix = ".9%", Icon = "bi-activity", SortOrder = 2 },
                new StatCounter { Label = "Years Experience", Value = 10, Suffix = "+", Icon = "bi-award", SortOrder = 3 },
                new StatCounter { Label = "Data Centers", Value = 8, Suffix = "", Icon = "bi-globe", SortOrder = 4 });
        }

        if (!db.Testimonials.Any())
        {
            db.Testimonials.AddRange(
                new Testimonial { Name = "Aysel Mammadova", Company = "Baku Digital", Rating = 5, SortOrder = 1,
                    Content = "Migrating our agency's clients here was the best decision we made this year. The interface is clean and our team learned it in a day." },
                new Testimonial { Name = "James Carter", Company = "CarterWeb", Rating = 5, SortOrder = 2,
                    Content = "Rock-solid uptime and the support team actually knows what they're talking about. Highly recommended." },
                new Testimonial { Name = "Priya Nair", Company = "Freelancer", Rating = 4, SortOrder = 3,
                    Content = "Everything I need to host my client sites in one place. Free SSL and backups just work." });
        }

        if (!db.VpsPlans.Any())
        {
            db.VpsPlans.AddRange(
                new VpsPlan { Name = "VPS Starter", CpuCores = 1, RamMB = 2048, DiskGB = 40, BandwidthGB = 2000, Price = 6.99m, Location = "Frankfurt, DE", SortOrder = 1 },
                new VpsPlan { Name = "VPS Business", CpuCores = 2, RamMB = 4096, DiskGB = 80, BandwidthGB = 4000, Price = 12.99m, Location = "Frankfurt, DE", IsPopular = true, SortOrder = 2 },
                new VpsPlan { Name = "VPS Pro", CpuCores = 4, RamMB = 8192, DiskGB = 160, BandwidthGB = 8000, Price = 24.99m, Location = "New York, US", SortOrder = 3 });
        }

        await db.SaveChangesAsync();

        if (!db.BlogCategories.Any())
        {
            var news = new BlogCategory { Name = "News", Slug = "news", Description = "Product news and announcements." };
            var guides = new BlogCategory { Name = "Guides", Slug = "guides", Description = "Tutorials and how-tos." };
            db.BlogCategories.AddRange(news, guides);
            await db.SaveChangesAsync();

            var tagHosting = new BlogTag { Name = "Hosting", Slug = "hosting" };
            var tagSecurity = new BlogTag { Name = "Security", Slug = "security" };
            db.BlogTags.AddRange(tagHosting, tagSecurity);
            await db.SaveChangesAsync();

            db.BlogPosts.AddRange(
                new BlogPost
                {
                    Title = "Welcome to our new hosting platform", Slug = "welcome-to-our-hosting",
                    Excerpt = "Fast, reliable hosting with everything you need to launch your website in minutes.",
                    Content = "<p>We're thrilled to welcome you to our hosting platform. Get your website online in minutes with everything you need in one place.</p><h3>What's included</h3><ul><li>Domains, email, databases, DNS and SSL</li><li>Daily backups and one-click restores</li><li>Friendly 24/7 support</li></ul>",
                    Status = BlogStatus.Published, PublishedAt = DateTime.UtcNow.AddDays(-5), AuthorId = admin?.Id,
                    CategoryId = news.Id, Tags = new List<BlogTag> { tagHosting }
                },
                new BlogPost
                {
                    Title = "5 Ways to Secure Your Hosting Account", Slug = "5-ways-to-secure-your-hosting-account",
                    Excerpt = "Simple, effective steps to keep your websites and email safe.",
                    Content = "<p>Security doesn't have to be complicated. Here are five things you can do today.</p><ol><li>Enable free SSL on every domain</li><li>Use strong, unique passwords</li><li>Keep your apps updated</li><li>Turn on daily backups</li><li>Enable two-factor authentication</li></ol>",
                    Status = BlogStatus.Published, PublishedAt = DateTime.UtcNow.AddDays(-2), AuthorId = admin?.Id,
                    CategoryId = guides.Id, Tags = new List<BlogTag> { tagSecurity, tagHosting }
                });
            await db.SaveChangesAsync();
        }

        if (!db.KbCategories.Any())
        {
            var getting = new KbCategory { Name = "Getting Started", Slug = "getting-started", Icon = "bi-rocket-takeoff", Description = "New here? Start with these.", SortOrder = 1 };
            var domains = new KbCategory { Name = "Domains & DNS", Slug = "domains-dns", Icon = "bi-globe2", Description = "Manage domains, subdomains and DNS.", SortOrder = 2 };
            var email = new KbCategory { Name = "Email", Slug = "email", Icon = "bi-envelope", Description = "Set up and manage mailboxes.", SortOrder = 3 };
            db.KbCategories.AddRange(getting, domains, email);
            await db.SaveChangesAsync();

            db.KbArticles.AddRange(
                new KbArticle
                {
                    Title = "How to log in to your control panel", Slug = "how-to-log-in", CategoryId = getting.Id, IsPublished = true,
                    Content = "<h2>Logging in</h2><p>Visit your panel URL and click <strong>Sign In</strong>. Enter the username and password from your welcome email.</p><h2>Forgot your password?</h2><p>Use the <em>Forgot password</em> link on the login page to reset it.</p>"
                },
                new KbArticle
                {
                    Title = "Pointing your domain to our servers", Slug = "pointing-your-domain", CategoryId = domains.Id, IsPublished = true,
                    Content = "<h2>Update your nameservers</h2><p>At your registrar, set the nameservers to the ones shown on your dashboard.</p><h2>Using A records</h2><p>Alternatively, create an <code>A</code> record pointing to your server IP.</p>"
                },
                new KbArticle
                {
                    Title = "Creating your first email account", Slug = "creating-your-first-email", CategoryId = email.Id, IsPublished = true,
                    Content = "<h2>Add a mailbox</h2><p>Go to <strong>Email</strong>, click <strong>Create</strong>, choose a domain and set a password. Your mailbox is ready instantly.</p>"
                });
            await db.SaveChangesAsync();
        }

        if (!db.Addons.Any())
        {
            db.Addons.AddRange(
                new Addon { Name = "Extra 5 GB Disk", Description = "Add 5 GB of NVMe SSD storage.", Type = AddonType.ExtraDisk, Value = 5120, Price = 2.00m, SortOrder = 1 },
                new Addon { Name = "Extra 10 GB Disk", Description = "Add 10 GB of NVMe SSD storage.", Type = AddonType.ExtraDisk, Value = 10240, Price = 3.50m, SortOrder = 2 },
                new Addon { Name = "Extra 50 GB Disk", Description = "Add 50 GB of NVMe SSD storage.", Type = AddonType.ExtraDisk, Value = 51200, Price = 12.00m, SortOrder = 3 },
                new Addon { Name = "Extra 100 GB Bandwidth", Description = "Add 100 GB of monthly bandwidth.", Type = AddonType.ExtraBandwidth, Value = 102400, Price = 2.50m, SortOrder = 4 },
                new Addon { Name = "10 Extra Email Accounts", Description = "Add 10 professional mailboxes.", Type = AddonType.ExtraEmail, Value = 10, Price = 1.50m, SortOrder = 5 },
                new Addon { Name = "5 Extra Databases", Description = "Add 5 MySQL databases.", Type = AddonType.ExtraDatabase, Value = 5, Price = 1.50m, SortOrder = 6 },
                new Addon { Name = "Dedicated IP Address", Description = "A dedicated IPv4 address for your account.", Type = AddonType.DedicatedIp, Value = 1, Price = 4.00m, SortOrder = 7 },
                new Addon { Name = "Premium SSL Certificate", Description = "A premium organisation-validated SSL certificate.", Type = AddonType.PremiumSsl, Value = 1, Price = 6.00m, SortOrder = 8 },
                new Addon { Name = "Daily Backups", Description = "Automated daily backups with one-click restore.", Type = AddonType.DailyBackup, Value = 0, Price = 3.00m, SortOrder = 9 },
                new Addon { Name = "Priority Support", Description = "Front-of-queue support with faster response times.", Type = AddonType.PrioritySupport, Value = 0, Price = 5.00m, SortOrder = 10 });
            await db.SaveChangesAsync();
        }

        // Upgrade any legacy demo content that still references the project name so
        // the public site stays fully white-labelled.
        var legacyTestimonials = db.Testimonials.Where(t => t.Content.Contains("SRXPanel")).ToList();
        foreach (var t in legacyTestimonials)
            t.Content = t.Content.Replace("SRXPanel ", "our platform ").Replace("SRXPanel", "our platform");

        var legacyPost = db.BlogPosts.FirstOrDefault(p => p.Slug == "introducing-srxpanel-1-0");
        if (legacyPost != null)
        {
            legacyPost.Title = "Welcome to our new hosting platform";
            legacyPost.Slug = "welcome-to-our-hosting";
            legacyPost.Excerpt = "Fast, reliable hosting with everything you need to launch your website in minutes.";
            legacyPost.Content = "<p>We're thrilled to welcome you to our hosting platform. Get your website online in minutes with everything you need in one place.</p><h3>What's included</h3><ul><li>Domains, email, databases, DNS and SSL</li><li>Daily backups and one-click restores</li><li>Friendly 24/7 support</li></ul>";
        }
        if (legacyTestimonials.Count > 0 || legacyPost != null)
            await db.SaveChangesAsync();
    }
}
