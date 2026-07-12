using Microsoft.Extensions.Options;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Integration;

public class NginxService : INginxService
{
    private const string ServiceName = "nginx";
    private readonly ICommandRunner _runner;
    private readonly PanelSettings _settings;

    public NginxService(ICommandRunner runner, IOptionsMonitor<PanelSettings> settings)
    {
        _runner = runner;
        _settings = settings.CurrentValue;
    }

    private string ConfPath(string domain) => $"{_settings.Nginx.SitesAvailable}/{domain}.conf";
    private string EnabledPath(string domain) => $"{_settings.Nginx.SitesEnabled}/{domain}.conf";

    public async Task<ServiceResult> CreateVirtualHostAsync(string domain, string documentRoot, string phpVersion)
    {
        var result = new ServiceResult { Message = $"Virtual host created for {domain}." };

        var conf = BuildVirtualHostTemplate(domain, documentRoot, phpVersion, sslEnabled: false, null, null);
        result.Commands.Add(await _runner.WriteFileAsync(ConfPath(domain), conf, ServiceName));

        // Enable site: symlink sites-available -> sites-enabled
        result.Commands.Add(await _runner.CreateSymlinkAsync(ConfPath(domain), EnabledPath(domain), ServiceName));

        // Test config, then reload
        var test = await _runner.RunAsync("nginx -t", ServiceName);
        result.Commands.Add(test);
        if (!test.Success)
        {
            result.Success = false;
            result.Message = $"nginx -t failed for {domain}. Site not reloaded.";
            return result;
        }

        result.Commands.Add(await _runner.RunAsync("systemctl reload nginx", ServiceName));
        return result;
    }

    public async Task<ServiceResult> DeleteVirtualHostAsync(string domain)
    {
        var result = new ServiceResult { Message = $"Virtual host removed for {domain}." };
        result.Commands.Add(await _runner.DeleteFileAsync(EnabledPath(domain), ServiceName));
        result.Commands.Add(await _runner.DeleteFileAsync(ConfPath(domain), ServiceName));
        result.Commands.Add(await _runner.RunAsync("systemctl reload nginx", ServiceName));
        return result;
    }

    public async Task<ServiceResult> EnableSslAsync(string domain, string certPath, string keyPath)
    {
        var result = new ServiceResult { Message = $"SSL enabled for {domain}." };
        // Re-write the vhost with SSL server block enabled.
        var conf = BuildVirtualHostTemplate(domain, $"/var/www/{domain}/public_html", _settings.DefaultPhpVersion,
            sslEnabled: true, certPath, keyPath);
        result.Commands.Add(await _runner.WriteFileAsync(ConfPath(domain), conf, ServiceName));

        var test = await _runner.RunAsync("nginx -t", ServiceName);
        result.Commands.Add(test);
        if (!test.Success)
        {
            result.Success = false;
            result.Message = $"nginx -t failed enabling SSL for {domain}.";
            return result;
        }
        result.Commands.Add(await _runner.RunAsync("systemctl reload nginx", ServiceName));
        return result;
    }

    public async Task<ServiceResult> DisableSslAsync(string domain)
    {
        var result = new ServiceResult { Message = $"SSL disabled for {domain}." };
        var conf = BuildVirtualHostTemplate(domain, $"/var/www/{domain}/public_html", _settings.DefaultPhpVersion,
            sslEnabled: false, null, null);
        result.Commands.Add(await _runner.WriteFileAsync(ConfPath(domain), conf, ServiceName));
        result.Commands.Add(await _runner.RunAsync("systemctl reload nginx", ServiceName));
        return result;
    }

    public async Task<ServiceResult> GetStatusAsync()
    {
        var cmd = await _runner.RunAsync("systemctl status nginx --no-pager", ServiceName);
        return new ServiceResult
        {
            Success = cmd.Success,
            Message = cmd.Simulated ? "nginx status (simulated)" : cmd.Output,
            Commands = { cmd }
        };
    }

    public async Task<ServiceResult> ReloadNginxAsync()
    {
        var test = await _runner.RunAsync("nginx -t", ServiceName);
        if (!test.Success)
        {
            return ServiceResult.Fail("nginx -t failed; not reloaded.", test);
        }
        var reload = await _runner.RunAsync("systemctl reload nginx", ServiceName);
        return new ServiceResult { Success = reload.Success, Message = "nginx reloaded.", Commands = { test, reload } };
    }

    private string BuildVirtualHostTemplate(string domain, string documentRoot, string phpVersion,
        bool sslEnabled, string? certPath, string? keyPath)
    {
        var phpSocket = _settings.Nginx.PhpFpmSocketPattern.Replace("{version}", phpVersion);
        var accessLog = $"{_settings.Nginx.LogDir}/{domain}.access.log";
        var errorLog = $"{_settings.Nginx.LogDir}/{domain}.error.log";

        var securityHeaders = """
                # Security headers
                add_header X-Frame-Options "SAMEORIGIN" always;
                add_header X-XSS-Protection "1; mode=block" always;
                add_header X-Content-Type-Options "nosniff" always;
                add_header Referrer-Policy "strict-origin-when-cross-origin" always;
                add_header Permissions-Policy "geolocation=(), microphone=(), camera=()" always;
        """;

        var gzip = """
                # Gzip compression (fallback when the client doesn't support Brotli)
                gzip on;
                gzip_vary on;
                gzip_min_length 1024;
                gzip_proxied any;
                gzip_types text/plain text/css text/xml application/json application/javascript application/xml+rss image/svg+xml;
        """;

        var brotli = """
                # Brotli compression (requires the ngx_brotli module)
                brotli on;
                brotli_comp_level 6;
                brotli_types text/plain text/css application/json application/javascript text/xml application/xml application/xml+rss image/svg+xml;
        """;

        var phpBlock = $$"""
                location ~ \.php$ {
                    include snippets/fastcgi-php.conf;
                    fastcgi_pass unix:{{phpSocket}};
                    fastcgi_param SCRIPT_FILENAME $document_root$fastcgi_script_name;
                }
        """;

        var commonBody = $$"""
                root {{documentRoot}};
                index index.php index.html index.htm;

                access_log {{accessLog}};
                error_log {{errorLog}};

        {{brotli}}

        {{gzip}}

        {{securityHeaders}}

                location / {
                    try_files $uri $uri/ /index.php?$query_string;
                }

        {{phpBlock}}

                location ~ /\.(?!well-known).* {
                    deny all;
                }
        """;

        var httpServer = $$"""
        # Managed by SRXPanel - {{domain}}
        server {
                listen 80;
                listen [::]:80;
                server_name {{domain}} www.{{domain}};

        {{(sslEnabled ? $"        # Redirect all HTTP traffic to HTTPS\n        return 301 https://$host$request_uri;" : commonBody)}}
        }
        """;

        if (!sslEnabled)
        {
            // SSL-ready but disabled: include a commented HTTPS block template.
            return httpServer + "\n\n" + $$"""
        # SSL server block (disabled - enabled automatically when a certificate is issued)
        # server {
        #     listen 443 ssl http2;
        #     server_name {{domain}} www.{{domain}};
        #     ssl_certificate     /etc/letsencrypt/live/{{domain}}/fullchain.pem;
        #     ssl_certificate_key /etc/letsencrypt/live/{{domain}}/privkey.pem;
        # }
        """ + "\n";
        }

        var sslServer = $$"""
        server {
                listen 443 ssl http2;
                listen [::]:443 ssl http2;
                # HTTP/3 (QUIC) — requires an nginx build with the http_v3 module.
                listen 443 quic reuseport;
                listen [::]:443 quic reuseport;
                server_name {{domain}} www.{{domain}};

                ssl_certificate     {{certPath}};
                ssl_certificate_key {{keyPath}};
                ssl_protocols TLSv1.2 TLSv1.3;
                ssl_ciphers HIGH:!aNULL:!MD5;
                ssl_prefer_server_ciphers on;

                # Advertise HTTP/3 availability to clients.
                add_header Alt-Svc 'h3=":443"; ma=86400' always;

        {{commonBody}}
        }
        """;

        return httpServer + "\n\n" + sslServer + "\n";
    }
}
