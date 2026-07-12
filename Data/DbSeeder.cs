using Microsoft.AspNetCore.Identity;
using SRXPanel.Models;

namespace SRXPanel.Data;

/// <summary>
/// Minimal production seed: roles, the admin account, two default hosting
/// packages, and the singleton configuration rows the app needs to run.
/// No demo/sample data (users, nodes, VPS, apps, blog, etc.) is created — those
/// are managed by the operator from the admin panel.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var db = services.GetRequiredService<ApplicationDbContext>();

        // ---- Roles ----
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        // ---- Two default hosting packages ----
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
                });
            await db.SaveChangesAsync();
        }

        // ---- Admin user ----
        // The password comes from the SRXPANEL_ADMIN_PASSWORD environment variable
        // (set by the installer from the operator's chosen password). It falls back
        // to a default only for local/dev use.
        var adminUser = await userManager.FindByNameAsync("admin");
        if (adminUser == null)
        {
            adminUser = new ApplicationUser
            {
                UserName = "admin",
                Email = "admin@srxpanel.local",
                FullName = "System Administrator",
                EmailConfirmed = true,
                IsActive = true,
                DiskQuotaMB = 0,
                BandwidthQuotaMB = 0,
                CreatedAt = DateTime.UtcNow
            };

            var adminPassword = Environment.GetEnvironmentVariable("SRXPANEL_ADMIN_PASSWORD");
            if (string.IsNullOrWhiteSpace(adminPassword))
                adminPassword = "Admin@123456!";

            var result = await userManager.CreateAsync(adminUser, adminPassword);
            if (result.Succeeded)
                await userManager.AddToRoleAsync(adminUser, Roles.SuperAdmin);
        }

        // ---- Singleton configuration (not demo data; the app needs these rows) ----
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

        if (!db.SecuritySettings.Any())
            db.SecuritySettings.Add(new SecuritySettings { Id = 1, UpdatedAt = DateTime.UtcNow });

        if (!db.AppUpdateSettings.Any())
            db.AppUpdateSettings.Add(new AppUpdateSettings { Id = 1, UpdatedAt = DateTime.UtcNow });

        if (!db.FrontendSettings.Any())
            db.FrontendSettings.Add(new FrontendSettings { Id = 1, UpdatedAt = DateTime.UtcNow });

        // Currency reference data (used by billing/currency dropdowns). Exchange
        // rates are populated by the daily RefreshExchangeRatesJob, not seeded.
        if (!db.Currencies.Any())
        {
            db.Currencies.AddRange(
                new Currency { Code = "usd", Name = "US Dollar", Symbol = "$", IsEnabled = true },
                new Currency { Code = "eur", Name = "Euro", Symbol = "€", IsEnabled = true },
                new Currency { Code = "gbp", Name = "British Pound", Symbol = "£", IsEnabled = true });
        }

        await db.SaveChangesAsync();
    }
}
