using System.Text;

namespace SRXPanel.Services.Developer;

/// <summary>
/// Seeds a plausible log file the first time a log is opened on a host where nginx,
/// php-fpm and MySQL are not actually running. On a real server the files already exist
/// and this never runs. The content is deterministic per (kind, domain) so the viewer,
/// the search filter and the level filter all have something consistent to work against.
/// </summary>
internal static class LogSampleGenerator
{
    public static string Generate(LogKind kind, string? domain)
    {
        var host = domain ?? "example.com";
        var seed = HashCode.Combine(kind, host);
        var random = new Random(seed);
        var start = DateTime.UtcNow.AddHours(-6);

        return kind switch
        {
            LogKind.PhpError => PhpError(random, start),
            LogKind.NginxAccess => NginxAccess(random, start, host),
            LogKind.NginxError => NginxError(random, start, host),
            LogKind.Application => Application(random, start),
            LogKind.MySqlSlowQuery => MySqlSlow(random, start),
            _ => ""
        };
    }

    private static readonly string[] Paths =
    {
        "/", "/index.php", "/about", "/wp-admin/admin-ajax.php", "/api/v1/products",
        "/assets/app.css", "/assets/app.js", "/favicon.ico", "/robots.txt", "/contact"
    };

    private static readonly string[] Agents =
    {
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/122.0 Safari/537.36",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 Safari/605.1.15",
        "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)",
        "curl/8.5.0"
    };

    private static string Ip(Random random) =>
        $"{random.Next(20, 220)}.{random.Next(0, 255)}.{random.Next(0, 255)}.{random.Next(1, 254)}";

    private static string NginxAccess(Random random, DateTime start, string host)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 240; i++)
        {
            var at = start.AddSeconds(i * 88 + random.Next(0, 40));
            var path = Paths[random.Next(Paths.Length)];
            var status = random.Next(100) switch
            {
                < 84 => 200,
                < 89 => 304,
                < 94 => 404,
                < 97 => 301,
                < 99 => 403,
                _ => 500
            };
            var bytes = status == 200 ? random.Next(400, 48000) : random.Next(0, 800);
            var method = path.StartsWith("/api") && random.Next(2) == 0 ? "POST" : "GET";

            sb.AppendLine($"{Ip(random)} - - [{at:dd/MMM/yyyy:HH:mm:ss} +0000] " +
                          $"\"{method} {path} HTTP/1.1\" {status} {bytes} \"https://{host}/\" \"{Agents[random.Next(Agents.Length)]}\"");
        }
        return sb.ToString();
    }

    private static string NginxError(Random random, DateTime start, string host)
    {
        var samples = new (string level, string message)[]
        {
            ("error", $"open() \"/var/www/{host}/public_html/favicon.ico\" failed (2: No such file or directory)"),
            ("warn", "an upstream response is buffered to a temporary file /var/cache/nginx/proxy_temp/1/00/0000000001"),
            ("error", "upstream timed out (110: Connection timed out) while reading response header from upstream"),
            ("notice", "signal process started"),
            ("crit", "SSL_do_handshake() failed (SSL: error:0A00006C:SSL routines::bad key share)"),
            ("error", "FastCGI sent in stderr: \"PHP message: PHP Fatal error: Allowed memory size exhausted\""),
            ("warn", "conflicting server name \"www." + host + "\" on 0.0.0.0:80, ignored")
        };

        var sb = new StringBuilder();
        for (var i = 0; i < 60; i++)
        {
            var at = start.AddMinutes(i * 6 + random.Next(0, 4));
            var (level, message) = samples[random.Next(samples.Length)];
            sb.AppendLine($"{at:yyyy/MM/dd HH:mm:ss} [{level}] {random.Next(1000, 9999)}#0: *{random.Next(1, 900)} {message}, " +
                          $"client: {Ip(random)}, server: {host}, request: \"GET / HTTP/1.1\", host: \"{host}\"");
        }
        return sb.ToString();
    }

    private static string PhpError(Random random, DateTime start)
    {
        var samples = new (string level, string message)[]
        {
            ("Warning", "Undefined array key \"page\" in /public_html/index.php on line 42"),
            ("Notice", "Only variables should be passed by reference in /public_html/lib/helpers.php on line 18"),
            ("Deprecated", "Automatic conversion of false to array is deprecated in /public_html/lib/cart.php on line 91"),
            ("Fatal error", "Uncaught PDOException: SQLSTATE[HY000] [2002] Connection refused in /public_html/db.php:12"),
            ("Warning", "file_get_contents(https://api.example.com/rates): Failed to open stream: HTTP request failed"),
            ("Notice", "session_start(): Ignoring session_start() because a session is already active")
        };

        var sb = new StringBuilder();
        for (var i = 0; i < 45; i++)
        {
            var at = start.AddMinutes(i * 8 + random.Next(0, 5));
            var (level, message) = samples[random.Next(samples.Length)];
            sb.AppendLine($"[{at:dd-MMM-yyyy HH:mm:ss} UTC] PHP {level}:  {message}");
        }
        return sb.ToString();
    }

    private static string Application(Random random, DateTime start)
    {
        var samples = new (string level, string message)[]
        {
            ("INFO", "Cache warmed in 214ms"),
            ("INFO", "Queue worker started, listening on default"),
            ("WARNING", "Slow response: GET /api/v1/products took 2841ms"),
            ("ERROR", "Failed to dispatch webhook to https://hooks.example.com/inbound: connection reset"),
            ("INFO", "Scheduled task report:generate finished"),
            ("NOTICE", "Config cache is stale — run config:cache")
        };

        var sb = new StringBuilder();
        for (var i = 0; i < 70; i++)
        {
            var at = start.AddMinutes(i * 5 + random.Next(0, 3));
            var (level, message) = samples[random.Next(samples.Length)];
            sb.AppendLine($"[{at:yyyy-MM-dd HH:mm:ss}] production.{level}: {message}");
        }
        return sb.ToString();
    }

    private static string MySqlSlow(Random random, DateTime start)
    {
        var queries = new[]
        {
            "SELECT * FROM orders WHERE customer_id = 4821 ORDER BY created_at DESC",
            "SELECT p.*, c.name FROM products p JOIN categories c ON c.id = p.category_id WHERE p.active = 1",
            "UPDATE sessions SET payload = '...' WHERE id = 'a91f...'",
            "SELECT COUNT(*) FROM page_views WHERE created_at > DATE_SUB(NOW(), INTERVAL 30 DAY)"
        };

        var sb = new StringBuilder();
        sb.AppendLine("/usr/sbin/mysqld, Version: 8.0.36. started with:");
        sb.AppendLine("Time                 Id Command    Argument");

        for (var i = 0; i < 24; i++)
        {
            var at = start.AddMinutes(i * 15 + random.Next(0, 8));
            var queryTime = 1.0 + random.NextDouble() * 8;
            var rowsExamined = random.Next(20_000, 900_000);

            sb.AppendLine($"# Time: {at:yyyy-MM-ddTHH:mm:ss}.000000Z");
            sb.AppendLine($"# User@Host: appuser[appuser] @ localhost []  Id: {random.Next(100, 999)}");
            sb.AppendLine($"# Query_time: {queryTime:0.000000}  Lock_time: 0.000{random.Next(100, 999)} " +
                          $"Rows_sent: {random.Next(1, 500)}  Rows_examined: {rowsExamined}");
            sb.AppendLine($"SET timestamp={new DateTimeOffset(at, TimeSpan.Zero).ToUnixTimeSeconds()};");
            sb.AppendLine($"{queries[random.Next(queries.Length)]};");
        }
        return sb.ToString();
    }
}
