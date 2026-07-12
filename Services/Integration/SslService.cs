using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Integration;

/// <summary>
/// Issues/renews certificates via certbot (Let's Encrypt) or openssl (self-signed),
/// and keeps the SslCertificate entity in the panel DB in sync.
/// </summary>
public class SslService : ISslService
{
    private const string ServiceName = "certbot";
    private readonly ICommandRunner _runner;
    private readonly ApplicationDbContext _db;

    public SslService(ICommandRunner runner, ApplicationDbContext db)
    {
        _runner = runner;
        _db = db;
    }

    public async Task<ServiceResult> IssueLetsEncryptAsync(string domain, string email)
    {
        var result = new ServiceResult { Message = $"Let's Encrypt certificate issued for {domain}." };

        var cmd = await _runner.RunAsync(
            $"certbot --nginx --non-interactive --agree-tos -m {email} -d {domain} -d www.{domain}",
            ServiceName);
        result.Commands.Add(cmd);

        if (!cmd.Success)
        {
            result.Success = false;
            result.Message = $"certbot failed for {domain}.";
            return result;
        }

        var certPath = $"/etc/letsencrypt/live/{domain}/fullchain.pem";
        var keyPath = $"/etc/letsencrypt/live/{domain}/privkey.pem";
        await UpsertCertAsync(domain, SslCertType.LetsEncrypt, DateTime.UtcNow.AddDays(90), certPath, keyPath);
        return result;
    }

    public async Task<ServiceResult> RenewCertificateAsync(string domain)
    {
        var result = new ServiceResult { Message = $"Certificate renewed for {domain}." };
        var cmd = await _runner.RunAsync($"certbot renew --cert-name {domain} --non-interactive", ServiceName);
        result.Commands.Add(cmd);

        if (cmd.Success)
        {
            var cert = await _db.SslCertificates.Include(c => c.Domain)
                .FirstOrDefaultAsync(c => c.Domain != null && c.Domain.DomainName == domain);
            if (cert != null)
            {
                cert.IssuedAt = DateTime.UtcNow;
                cert.ExpiresAt = DateTime.UtcNow.AddDays(cert.Type == SslCertType.LetsEncrypt ? 90 : 365);
                cert.Status = SslCertStatus.Active;
                await _db.SaveChangesAsync();
            }
        }
        else
        {
            result.Success = false;
            result.Message = $"Renewal failed for {domain}.";
        }
        return result;
    }

    public async Task<ServiceResult> IssueSelfSignedAsync(string domain)
    {
        var result = new ServiceResult { Message = $"Self-signed certificate issued for {domain}." };
        var dir = $"/etc/ssl/srxpanel/{domain}";
        var certPath = $"{dir}/fullchain.pem";
        var keyPath = $"{dir}/privkey.pem";

        result.Commands.Add(await _runner.RunAsync($"mkdir -p {dir}", ServiceName));
        var cmd = await _runner.RunAsync(
            $"openssl req -x509 -nodes -days 365 -newkey rsa:2048 " +
            $"-keyout {keyPath} -out {certPath} -subj \"/CN={domain}/O=SRXPanel\"",
            ServiceName);
        result.Commands.Add(cmd);

        if (!cmd.Success)
        {
            result.Success = false;
            result.Message = $"openssl failed for {domain}.";
            return result;
        }

        await UpsertCertAsync(domain, SslCertType.SelfSigned, DateTime.UtcNow.AddDays(365), certPath, keyPath);
        return result;
    }

    public async Task<DateTime?> GetExpiryDateAsync(string domain)
    {
        if (_runner.SimulationMode)
        {
            var cert = await _db.SslCertificates.Include(c => c.Domain)
                .FirstOrDefaultAsync(c => c.Domain != null && c.Domain.DomainName == domain);
            return cert?.ExpiresAt;
        }

        var cmd = await _runner.RunAsync(
            $"openssl x509 -enddate -noout -in /etc/letsencrypt/live/{domain}/fullchain.pem | cut -d= -f2",
            ServiceName);
        return DateTime.TryParse(cmd.Output.Trim(), out var dt) ? dt : null;
    }

    private async Task UpsertCertAsync(string domain, SslCertType type, DateTime expires, string certPath, string keyPath)
    {
        var domainEntity = await _db.Domains.FirstOrDefaultAsync(d => d.DomainName == domain);
        if (domainEntity == null) return;

        var existing = await _db.SslCertificates.Where(c => c.DomainId == domainEntity.Id).ToListAsync();
        _db.SslCertificates.RemoveRange(existing);

        _db.SslCertificates.Add(new SslCertificate
        {
            DomainId = domainEntity.Id,
            UserId = domainEntity.UserId,
            Type = type,
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = expires,
            Status = SslCertStatus.Active,
            CertificatePath = certPath,
            KeyPath = keyPath
        });
        domainEntity.SslEnabled = true;
        await _db.SaveChangesAsync();
    }
}
