using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
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
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

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
        // The initial SuperAdmin password ALWAYS comes from the
        // SRXPANEL_ADMIN_PASSWORD environment variable (the installer exports the
        // operator's chosen password). Only when that variable is missing/empty do
        // we fall back to a *randomly generated* password, which is written to the
        // log clearly — we never silently seed a fixed, well-known default.
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

            var envPassword = Environment.GetEnvironmentVariable("SRXPANEL_ADMIN_PASSWORD");
            var usingEnvPassword = !string.IsNullOrWhiteSpace(envPassword);
            var adminPassword = usingEnvPassword ? envPassword! : GenerateRandomPassword();

            var result = await userManager.CreateAsync(adminUser, adminPassword);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, Roles.SuperAdmin);

                if (usingEnvPassword)
                {
                    logger.LogInformation(
                        "Created initial admin account 'admin' using the password from SRXPANEL_ADMIN_PASSWORD.");
                }
                else
                {
                    logger.LogWarning(
                        "SRXPANEL_ADMIN_PASSWORD was not set. Generated a random admin password: {Password}",
                        adminPassword);
                    logger.LogWarning(
                        "Log in as 'admin' with the password above, then change it immediately.");
                }
            }
            else
            {
                var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                logger.LogError(
                    "Failed to create the initial admin account (source: {Source}): {Errors}",
                    usingEnvPassword ? "SRXPANEL_ADMIN_PASSWORD" : "generated", errors);
            }
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

    /// <summary>
    /// Builds a random password that satisfies the Identity password policy
    /// (upper, lower, digit, and special character; comfortably long). Used only
    /// as a fallback when SRXPANEL_ADMIN_PASSWORD is not provided. Ambiguous
    /// characters (0/O, 1/l/I) are omitted so it's safe to copy from a log.
    /// </summary>
    private static string GenerateRandomPassword()
    {
        const string upper = "ABCDEFGHJKMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnpqrstuvwxyz";
        const string digits = "23456789";
        const string special = "!@#%^_+=";
        const string all = upper + lower + digits + special;

        // Guarantee one character from each required class, then fill the rest.
        var chars = new List<char>
        {
            upper[RandomNumberGenerator.GetInt32(upper.Length)],
            lower[RandomNumberGenerator.GetInt32(lower.Length)],
            digits[RandomNumberGenerator.GetInt32(digits.Length)],
            special[RandomNumberGenerator.GetInt32(special.Length)],
        };
        while (chars.Count < 20)
            chars.Add(all[RandomNumberGenerator.GetInt32(all.Length)]);

        // Fisher–Yates shuffle so the guaranteed characters aren't always first.
        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars.ToArray());
    }
}
