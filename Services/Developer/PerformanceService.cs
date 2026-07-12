using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;

namespace SRXPanel.Services.Developer;

public record PerfRecommendation(string Title, string Detail, string Severity);

public record PerfResult(
    string Url,
    bool Reachable,
    string? Error,
    int StatusCode,
    long TtfbMs,
    long TotalMs,
    long? SslHandshakeMs,
    long ContentBytes,
    bool GzipEnabled,
    string? ContentEncoding,
    bool BrotliSupported,
    string? CacheControl,
    string? Etag,
    string? HttpVersion,
    Dictionary<string, string> Headers,
    List<PerfRecommendation> Recommendations,
    bool Simulated);

public interface IPerformanceService
{
    Task<PerfResult> TestAsync(string userId, int domainId, CancellationToken ct = default);
}

/// <summary>
/// Times a real HTTP request to the domain from the server side. When the domain does
/// not resolve (the normal case on a dev host) it reports that plainly and returns a
/// clearly-labelled simulated measurement rather than pretending the site is up.
/// </summary>
public class PerformanceService : IPerformanceService
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PerformanceService> _logger;

    public PerformanceService(ApplicationDbContext db, IHttpClientFactory httpClientFactory,
        ILogger<PerformanceService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<PerfResult> TestAsync(string userId, int domainId, CancellationToken ct = default)
    {
        var domain = await _db.Domains.FirstOrDefaultAsync(d => d.Id == domainId && d.UserId == userId, ct)
            ?? throw new InvalidOperationException("Domain not found.");

        var scheme = domain.SslEnabled ? "https" : "http";
        var url = $"{scheme}://{domain.DomainName}/";

        try
        {
            return await MeasureAsync(url, domain.SslEnabled, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or TaskCanceledException)
        {
            _logger.LogDebug(ex, "Performance test for {Url} could not reach the host", url);
            return Simulated(url, domain.SslEnabled, ex.Message);
        }
    }

    private async Task<PerfResult> MeasureAsync(string url, bool https, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("perf");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.AcceptEncoding.ParseAdd("gzip, deflate, br");

        var total = Stopwatch.StartNew();

        // ResponseHeadersRead stops the clock when the first byte of the response arrives.
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var ttfb = total.ElapsedMilliseconds;

        var body = await response.Content.ReadAsByteArrayAsync(ct);
        total.Stop();

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers)
            headers[header.Key] = string.Join(", ", header.Value);
        foreach (var header in response.Content.Headers)
            headers[header.Key] = string.Join(", ", header.Value);

        var encoding = headers.GetValueOrDefault("Content-Encoding");
        var cacheControl = headers.GetValueOrDefault("Cache-Control");
        var etag = headers.GetValueOrDefault("ETag");

        long? handshake = https ? await MeasureSslHandshakeAsync(new Uri(url).Host, ct) : null;

        var result = new PerfResult(
            url, true, null, (int)response.StatusCode, ttfb, total.ElapsedMilliseconds, handshake,
            body.LongLength,
            encoding?.Contains("gzip", StringComparison.OrdinalIgnoreCase) == true ||
            encoding?.Contains("br", StringComparison.OrdinalIgnoreCase) == true,
            encoding,
            encoding?.Contains("br", StringComparison.OrdinalIgnoreCase) == true,
            cacheControl, etag,
            $"HTTP/{response.Version}",
            headers,
            new List<PerfRecommendation>(),
            Simulated: false);

        return result with { Recommendations = BuildRecommendations(result) };
    }

    /// <summary>Times the TLS handshake alone, separate from the request.</summary>
    private async Task<long?> MeasureSslHandshakeAsync(string host, CancellationToken ct)
    {
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, 443, ct);

            var sw = Stopwatch.StartNew();
            await using var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, ct);
            sw.Stop();

            return sw.ElapsedMilliseconds;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The dev host cannot resolve customer domains; say so instead of inventing an "up" result.</summary>
    private static PerfResult Simulated(string url, bool https, string error)
    {
        var seed = url.Aggregate(17, (acc, c) => acc * 31 + c);
        var random = new Random(seed);

        var ttfb = random.Next(60, 320);
        var total = ttfb + random.Next(80, 600);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Server"] = "nginx",
            ["Content-Type"] = "text/html; charset=UTF-8",
            ["Content-Encoding"] = "gzip",
            ["Cache-Control"] = "no-cache, private",
            ["X-Frame-Options"] = "SAMEORIGIN",
            ["X-Content-Type-Options"] = "nosniff",
            ["Strict-Transport-Security"] = https ? "max-age=31536000" : ""
        };

        var result = new PerfResult(
            url, false, $"Could not reach {url} from this server ({error}). The numbers below are a simulated example.",
            200, ttfb, total, https ? random.Next(40, 160) : null,
            random.Next(12_000, 90_000),
            GzipEnabled: true, ContentEncoding: "gzip", BrotliSupported: false,
            CacheControl: "no-cache, private", Etag: null,
            HttpVersion: "HTTP/1.1",
            Headers: headers,
            Recommendations: new List<PerfRecommendation>(),
            Simulated: true);

        return result with { Recommendations = BuildRecommendations(result) };
    }

    private static List<PerfRecommendation> BuildRecommendations(PerfResult r)
    {
        var recommendations = new List<PerfRecommendation>();

        if (!r.GzipEnabled)
            recommendations.Add(new PerfRecommendation("Enable gzip compression",
                "No Content-Encoding was returned. Turn on gzip in the nginx vhost to cut HTML/CSS/JS transfer size by 60–80%.", "danger"));

        if (!r.BrotliSupported)
            recommendations.Add(new PerfRecommendation("Enable Brotli compression",
                "Brotli typically beats gzip by another 15–20% on text. Install ngx_brotli and add brotli on;", "warning"));

        if (string.IsNullOrWhiteSpace(r.CacheControl) ||
            r.CacheControl.Contains("no-cache", StringComparison.OrdinalIgnoreCase) ||
            r.CacheControl.Contains("no-store", StringComparison.OrdinalIgnoreCase))
            recommendations.Add(new PerfRecommendation("Set cache headers",
                "Static assets are being served without a cacheable Cache-Control. Add expires 30d; for images, CSS and JS.", "warning"));

        if (r.HttpVersion is not null && !r.HttpVersion.Contains("2") && !r.HttpVersion.Contains("3"))
            recommendations.Add(new PerfRecommendation("Enable HTTP/2",
                "The response used HTTP/1.1. Add http2 to the listen directive to multiplex requests over one connection.", "warning"));

        if (r.TtfbMs > 600)
            recommendations.Add(new PerfRecommendation("Reduce time to first byte",
                $"TTFB was {r.TtfbMs}ms. Anything over 600ms usually means slow database queries or no opcode cache — check the slow query log.", "danger"));

        if (r.SslHandshakeMs > 300)
            recommendations.Add(new PerfRecommendation("Speed up the TLS handshake",
                $"The handshake took {r.SslHandshakeMs}ms. Enable session resumption (ssl_session_cache) and OCSP stapling.", "warning"));

        if (r.ContentBytes > 500_000)
            recommendations.Add(new PerfRecommendation("Trim the HTML payload",
                $"The document is {r.ContentBytes / 1024}KB before assets. Consider lazy-loading below-the-fold content.", "warning"));

        if (recommendations.Count == 0)
            recommendations.Add(new PerfRecommendation("Nothing to fix",
                "Compression, caching and protocol version all look healthy.", "success"));

        return recommendations;
    }
}
