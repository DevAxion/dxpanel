using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Developer;

/// <summary>A php.ini directive shown read-only in the info table.</summary>
public record PhpDirective(string Name, string Value, bool Editable);

public record PhpExtension(string Name, string Version, bool Enabled);

public interface IPhpConfigService
{
    Task<PhpConfig> GetAsync(string userId, int domainId);
    Task<ServiceResult> SaveAsync(string userId, int domainId, PhpConfig input);

    /// <summary>The effective directives for a domain (user overrides layered over the pool defaults).</summary>
    Task<List<PhpDirective>> GetDirectivesAsync(string userId, int domainId);

    IReadOnlyList<PhpExtension> GetExtensions(string phpVersion);

    /// <summary>A filtered phpinfo() — the security-sensitive sections are omitted deliberately.</summary>
    Task<Dictionary<string, List<PhpDirective>>> GetPhpInfoAsync(string userId, int domainId);
}

public class PhpConfigService : IPhpConfigService
{
    private const string ServiceName = "php-fpm";

    /// <summary>Ceilings a shared-hosting user may not exceed.</summary>
    public const int MaxMemoryLimitMB = 512;
    public const int MaxExecutionTime = 300;
    public const int MaxUploadMB = 256;
    public const int MaxInputVars = 10000;

    public static readonly string[] ErrorReportingLevels =
    {
        "E_ALL",
        "E_ALL & ~E_DEPRECATED & ~E_STRICT",
        "E_ALL & ~E_NOTICE & ~E_DEPRECATED",
        "E_ERROR | E_WARNING | E_PARSE",
        "E_ERROR"
    };

    public static readonly string[] Timezones =
    {
        "UTC", "Europe/London", "Europe/Berlin", "Europe/Istanbul", "Asia/Baku", "Asia/Dubai",
        "Asia/Kolkata", "Asia/Singapore", "Asia/Tokyo", "America/New_York", "America/Chicago",
        "America/Denver", "America/Los_Angeles", "Australia/Sydney"
    };

    private readonly ApplicationDbContext _db;
    private readonly ICommandRunner _runner;

    public PhpConfigService(ApplicationDbContext db, ICommandRunner runner)
    {
        _db = db;
        _runner = runner;
    }

    private async Task<Domain> OwnedDomainAsync(string userId, int domainId) =>
        await _db.Domains.FirstOrDefaultAsync(d => d.Id == domainId && d.UserId == userId)
        ?? throw new InvalidOperationException("Domain not found.");

    public async Task<PhpConfig> GetAsync(string userId, int domainId)
    {
        await OwnedDomainAsync(userId, domainId);

        var config = await _db.PhpConfigs.FirstOrDefaultAsync(c => c.DomainId == domainId);
        if (config != null) return config;

        config = new PhpConfig { DomainId = domainId, UserId = userId };
        _db.PhpConfigs.Add(config);
        await _db.SaveChangesAsync();
        return config;
    }

    public async Task<ServiceResult> SaveAsync(string userId, int domainId, PhpConfig input)
    {
        var domain = await OwnedDomainAsync(userId, domainId);
        var config = await GetAsync(userId, domainId);

        foreach (var (name, value) in new (string, string)[]
                 {
                     ("memory_limit", input.MemoryLimit),
                     ("max_execution_time", input.MaxExecutionTime.ToString()),
                     ("upload_max_filesize", input.UploadMaxFilesize),
                     ("post_max_size", input.PostMaxSize),
                     ("max_input_vars", input.MaxInputVars.ToString())
                 })
        {
            var (ok, error) = Validate(name, value);
            if (!ok) throw new InvalidOperationException(error);
        }

        if (!Timezones.Contains(input.Timezone))
            throw new InvalidOperationException($"'{input.Timezone}' is not a supported timezone.");
        if (!ErrorReportingLevels.Contains(input.ErrorReporting))
            throw new InvalidOperationException("Unsupported error_reporting level.");

        config.MemoryLimit = input.MemoryLimit;
        config.MaxExecutionTime = input.MaxExecutionTime;
        config.UploadMaxFilesize = input.UploadMaxFilesize;
        config.PostMaxSize = input.PostMaxSize;
        config.MaxInputVars = input.MaxInputVars;
        config.Timezone = input.Timezone;
        config.DisplayErrors = input.DisplayErrors;
        config.ErrorReporting = input.ErrorReporting;
        config.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var user = await _db.Users.FirstAsync(u => u.Id == userId);
        var iniPath = $"/home/{user.UserName}/.php.ini";

        var write = await _runner.WriteFileAsync(iniPath, RenderIni(config, domain), ServiceName);
        var reload = await _runner.RunAsync(
            $"systemctl reload php{domain.PhpVersion}-fpm || service php{domain.PhpVersion}-fpm reload", ServiceName);

        return ServiceResult.Ok($"PHP settings saved for {domain.DomainName}.", write, reload);
    }

    /// <summary>The .php.ini the per-user PHP-FPM pool picks up.</summary>
    private static string RenderIni(PhpConfig config, Domain domain)
    {
        var sb = new StringBuilder();
        sb.AppendLine("; Generated by SRXPanel — edit from Developer → PHP Configuration.");
        sb.AppendLine($"; Domain: {domain.DomainName}   Updated: {config.UpdatedAt:u}");
        sb.AppendLine();
        sb.AppendLine($"memory_limit = {config.MemoryLimit}");
        sb.AppendLine($"max_execution_time = {config.MaxExecutionTime}");
        sb.AppendLine($"upload_max_filesize = {config.UploadMaxFilesize}");
        sb.AppendLine($"post_max_size = {config.PostMaxSize}");
        sb.AppendLine($"max_input_vars = {config.MaxInputVars}");
        sb.AppendLine($"date.timezone = {config.Timezone}");
        sb.AppendLine($"display_errors = {(config.DisplayErrors ? "On" : "Off")}");
        sb.AppendLine($"error_reporting = {config.ErrorReporting}");
        sb.AppendLine("log_errors = On");
        sb.AppendLine($"error_log = /home/logs/{domain.DomainName}.php-error.log");
        return sb.ToString();
    }

    /// <summary>Enforces the shared-hosting ceilings. Values arrive as php.ini shorthand ("256M").</summary>
    internal static (bool ok, string error) Validate(string name, string value)
    {
        switch (name)
        {
            case "memory_limit":
            {
                if (value.Trim() == "-1") return (false, "memory_limit may not be unlimited on shared hosting.");
                if (!TryParseBytes(value, out var mb)) return (false, "memory_limit must look like 256M.");
                return mb <= MaxMemoryLimitMB
                    ? (true, "")
                    : (false, $"memory_limit may not exceed {MaxMemoryLimitMB}M.");
            }
            case "max_execution_time":
            {
                if (!int.TryParse(value, out var seconds) || seconds < 5)
                    return (false, "max_execution_time must be at least 5 seconds.");
                return seconds <= MaxExecutionTime
                    ? (true, "")
                    : (false, $"max_execution_time may not exceed {MaxExecutionTime} seconds.");
            }
            case "upload_max_filesize":
            case "post_max_size":
            {
                if (!TryParseBytes(value, out var mb)) return (false, $"{name} must look like 64M.");
                return mb <= MaxUploadMB
                    ? (true, "")
                    : (false, $"{name} may not exceed {MaxUploadMB}M.");
            }
            case "max_input_vars":
            {
                if (!int.TryParse(value, out var vars) || vars < 100)
                    return (false, "max_input_vars must be at least 100.");
                return vars <= MaxInputVars
                    ? (true, "")
                    : (false, $"max_input_vars may not exceed {MaxInputVars}.");
            }
            default:
                return (true, "");
        }
    }

    /// <summary>Parses php.ini shorthand ("512K", "256M", "1G") into megabytes.</summary>
    private static bool TryParseBytes(string value, out int megabytes)
    {
        megabytes = 0;
        var match = Regex.Match(value.Trim(), @"^(\d+)\s*([KMG])?$", RegexOptions.IgnoreCase);
        if (!match.Success) return false;

        var number = int.Parse(match.Groups[1].Value);
        megabytes = (match.Groups[2].Value.ToUpperInvariant()) switch
        {
            "K" => Math.Max(1, number / 1024),
            "G" => number * 1024,
            "M" => number,
            _ => Math.Max(1, number / (1024 * 1024)) // plain bytes
        };
        return true;
    }

    public async Task<List<PhpDirective>> GetDirectivesAsync(string userId, int domainId)
    {
        var config = await GetAsync(userId, domainId);
        var domain = await OwnedDomainAsync(userId, domainId);

        return new List<PhpDirective>
        {
            new("memory_limit", config.MemoryLimit, true),
            new("max_execution_time", config.MaxExecutionTime.ToString(), true),
            new("upload_max_filesize", config.UploadMaxFilesize, true),
            new("post_max_size", config.PostMaxSize, true),
            new("max_input_vars", config.MaxInputVars.ToString(), true),
            new("date.timezone", config.Timezone, true),
            new("display_errors", config.DisplayErrors ? "On" : "Off", true),
            new("error_reporting", config.ErrorReporting, true),
            new("max_input_time", "60", false),
            new("default_socket_timeout", "60", false),
            new("session.gc_maxlifetime", "1440", false),
            new("realpath_cache_size", "4096K", false),
            new("opcache.enable", "On", false),
            new("opcache.memory_consumption", "128", false),
            new("disable_functions", "exec,passthru,shell_exec,system,proc_open,popen", false),
            new("open_basedir", $"/home/{(await _db.Users.FirstAsync(u => u.Id == userId)).UserName}:/tmp", false),
            new("php_version", domain.PhpVersion, false)
        };
    }

    public IReadOnlyList<PhpExtension> GetExtensions(string phpVersion)
    {
        var extensions = new List<PhpExtension>
        {
            new("Core", phpVersion, true), new("bcmath", phpVersion, true), new("calendar", phpVersion, true),
            new("ctype", phpVersion, true), new("curl", "8.5.0", true), new("date", phpVersion, true),
            new("dom", "20031129", true), new("exif", phpVersion, true), new("fileinfo", phpVersion, true),
            new("filter", phpVersion, true), new("ftp", phpVersion, true), new("gd", phpVersion, true),
            new("gettext", phpVersion, true), new("hash", phpVersion, true), new("iconv", phpVersion, true),
            new("imagick", "3.7.0", true), new("intl", phpVersion, true), new("json", phpVersion, true),
            new("libxml", "2.9.14", true), new("mbstring", phpVersion, true), new("mysqli", phpVersion, true),
            new("mysqlnd", "mysqlnd 8.3", true), new("openssl", "3.0.11", true), new("pcre", "10.42", true),
            new("PDO", phpVersion, true), new("pdo_mysql", phpVersion, true), new("pdo_sqlite", "3.44", true),
            new("Phar", phpVersion, true), new("posix", phpVersion, true), new("readline", phpVersion, true),
            new("redis", "6.0.2", true), new("Reflection", phpVersion, true), new("session", phpVersion, true),
            new("SimpleXML", phpVersion, true), new("soap", phpVersion, true), new("sodium", "2.0.23", true),
            new("SPL", phpVersion, true), new("sqlite3", "3.44", true), new("tokenizer", phpVersion, true),
            new("xml", phpVersion, true), new("xmlreader", phpVersion, true), new("xmlwriter", phpVersion, true),
            new("Zend OPcache", phpVersion, true), new("zip", "1.22.3", true), new("zlib", "1.2.13", true),
            new("xdebug", "3.3.1", false), new("memcached", "3.2.0", false)
        };
        return extensions;
    }

    public async Task<Dictionary<string, List<PhpDirective>>> GetPhpInfoAsync(string userId, int domainId)
    {
        var domain = await OwnedDomainAsync(userId, domainId);
        var config = await GetAsync(userId, domainId);

        // Deliberately filtered: no environment block, no full php.ini path list,
        // no loaded configuration files, no server variables. Those leak host details.
        return new Dictionary<string, List<PhpDirective>>
        {
            ["PHP Core"] = new()
            {
                new("PHP Version", domain.PhpVersion, false),
                new("System", "Linux srxpanel 6.1.0-18-amd64 x86_64", false),
                new("Server API", "FPM/FastCGI", false),
                new("Thread Safety", "disabled", false),
                new("Zend Signal Handling", "enabled", false),
                new("Zend Memory Manager", "enabled", false)
            },
            ["Resource Limits"] = new()
            {
                new("memory_limit", config.MemoryLimit, true),
                new("max_execution_time", config.MaxExecutionTime.ToString(), true),
                new("max_input_time", "60", false),
                new("max_input_vars", config.MaxInputVars.ToString(), true)
            },
            ["File Uploads"] = new()
            {
                new("file_uploads", "On", false),
                new("upload_max_filesize", config.UploadMaxFilesize, true),
                new("post_max_size", config.PostMaxSize, true),
                new("max_file_uploads", "20", false)
            },
            ["Error Handling"] = new()
            {
                new("display_errors", config.DisplayErrors ? "On" : "Off", true),
                new("display_startup_errors", "Off", false),
                new("error_reporting", config.ErrorReporting, true),
                new("log_errors", "On", false)
            },
            ["Date/Time"] = new()
            {
                new("date.timezone", config.Timezone, true),
                new("date.default_latitude", "31.7667", false),
                new("date.default_longitude", "35.2333", false)
            },
            ["OPcache"] = new()
            {
                new("opcache.enable", "On", false),
                new("opcache.memory_consumption", "128", false),
                new("opcache.max_accelerated_files", "10000", false),
                new("opcache.revalidate_freq", "2", false)
            }
        };
    }
}
