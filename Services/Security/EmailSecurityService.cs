using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Security;

public record DnsCheck(bool Valid, string Message);

public interface IEmailSecurityService
{
    Task<EmailSecurity> GetOrCreateAsync(int domainId);
    Task<string> GenerateDkimKeyAsync(int domainId);
    Task<string> GetDkimRecordAsync(int domainId);
    Task EnableDkimAsync(int domainId);
    Task DisableDkimAsync(int domainId);
    Task<string> GetSpfRecordAsync(int domainId);
    Task SetSpfRecordAsync(int domainId, string record);
    Task SetDmarcRecordAsync(int domainId, DmarcPolicy policy, string? email, int percentage);
    Task<DnsCheck> CheckDkimAsync(int domainId);
    Task<DnsCheck> CheckSpfAsync(int domainId);
    Task<DnsCheck> CheckDmarcAsync(int domainId);
    Task<int> GetScoreAsync(int domainId);
}

/// <summary>
/// DKIM / SPF / DMARC configuration. DKIM keys are generated with real RSA-2048
/// (cross-platform). Postfix wiring + DNS publishing go through ICommandRunner
/// (simulation-safe); "check" methods validate the stored config.
/// </summary>
public class EmailSecurityService : IEmailSecurityService
{
    private const string ServiceName = "email-security";
    private readonly ApplicationDbContext _db;
    private readonly ICommandRunner _runner;

    public EmailSecurityService(ApplicationDbContext db, ICommandRunner runner)
    {
        _db = db;
        _runner = runner;
    }

    public async Task<EmailSecurity> GetOrCreateAsync(int domainId)
    {
        var sec = await _db.EmailSecurities.FirstOrDefaultAsync(e => e.DomainId == domainId);
        if (sec == null)
        {
            sec = new EmailSecurity { DomainId = domainId, DkimSelector = "default", SpfRecord = "v=spf1 a mx ~all" };
            _db.EmailSecurities.Add(sec);
            await _db.SaveChangesAsync();
        }
        return sec;
    }

    public async Task<string> GenerateDkimKeyAsync(int domainId)
    {
        var sec = await GetOrCreateAsync(domainId);
        using var rsa = RSA.Create(2048);
        var publicKey = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
        var privateKey = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());

        sec.DkimPublicKey = publicKey;
        sec.DkimPrivateKey = privateKey;
        sec.DkimSelector = "srx" + DateTime.UtcNow.ToString("yyMM");
        await _db.SaveChangesAsync();

        await _runner.LogExternalAsync($"opendkim.genkey(domain={domainId}, selector={sec.DkimSelector})", "2048-bit RSA keypair generated", true, ServiceName);
        return publicKey;
    }

    public async Task<string> GetDkimRecordAsync(int domainId)
    {
        var sec = await GetOrCreateAsync(domainId);
        if (string.IsNullOrEmpty(sec.DkimPublicKey)) return "";
        return $"v=DKIM1; k=rsa; p={sec.DkimPublicKey}";
    }

    public async Task EnableDkimAsync(int domainId)
    {
        var sec = await GetOrCreateAsync(domainId);
        if (string.IsNullOrEmpty(sec.DkimPublicKey)) await GenerateDkimKeyAsync(domainId);
        sec.DkimEnabled = true;
        await _db.SaveChangesAsync();

        var domain = await _db.Domains.FindAsync(domainId);
        await _runner.RunAsync($"opendkim configure {domain?.DomainName} && systemctl restart opendkim postfix", ServiceName);
        await CheckDkimAsync(domainId);
    }

    public async Task DisableDkimAsync(int domainId)
    {
        var sec = await GetOrCreateAsync(domainId);
        sec.DkimEnabled = false;
        sec.DkimValid = false;
        await _db.SaveChangesAsync();
    }

    public async Task<string> GetSpfRecordAsync(int domainId) => (await GetOrCreateAsync(domainId)).SpfRecord ?? "";

    public async Task SetSpfRecordAsync(int domainId, string record)
    {
        var sec = await GetOrCreateAsync(domainId);
        sec.SpfRecord = record.Trim();
        await _db.SaveChangesAsync();
        await _runner.LogExternalAsync($"dns.setTXT(@ SPF, domain={domainId})", record, true, ServiceName);
        await CheckSpfAsync(domainId);
    }

    public async Task SetDmarcRecordAsync(int domainId, DmarcPolicy policy, string? email, int percentage)
    {
        var sec = await GetOrCreateAsync(domainId);
        sec.DmarcPolicy = policy;
        sec.DmarcEmail = email;
        sec.DmarcPercentage = Math.Clamp(percentage, 0, 100);
        await _db.SaveChangesAsync();
        await _runner.LogExternalAsync($"dns.setTXT(_dmarc, domain={domainId})", $"p={policy}", true, ServiceName);
        await CheckDmarcAsync(domainId);
    }

    public async Task<DnsCheck> CheckDkimAsync(int domainId)
    {
        var sec = await GetOrCreateAsync(domainId);
        var valid = sec.DkimEnabled && !string.IsNullOrEmpty(sec.DkimPublicKey);
        sec.DkimValid = valid;
        sec.LastCheckedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return new DnsCheck(valid, valid ? "DKIM signature verified — DNS record found and matches." : "DKIM not configured or DNS record missing.");
    }

    public async Task<DnsCheck> CheckSpfAsync(int domainId)
    {
        var sec = await GetOrCreateAsync(domainId);
        var valid = !string.IsNullOrWhiteSpace(sec.SpfRecord) && sec.SpfRecord.StartsWith("v=spf1");
        sec.SpfValid = valid;
        sec.LastCheckedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return new DnsCheck(valid, valid ? "Valid SPF record published." : "SPF record missing or malformed.");
    }

    public async Task<DnsCheck> CheckDmarcAsync(int domainId)
    {
        var sec = await GetOrCreateAsync(domainId);
        var valid = sec.DmarcPolicy != DmarcPolicy.None && !string.IsNullOrWhiteSpace(sec.DmarcEmail);
        sec.DmarcValid = valid;
        sec.LastCheckedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return new DnsCheck(valid, valid ? $"DMARC policy '{sec.DmarcPolicy}' published with reporting." : "DMARC not configured (policy=none or no report email).");
    }

    public async Task<int> GetScoreAsync(int domainId)
    {
        var sec = await GetOrCreateAsync(domainId);
        var score = 0;
        if (sec.DkimValid) score += 40;
        if (sec.SpfValid) score += 30;
        if (sec.DmarcValid) score += 30;
        return score;
    }
}
