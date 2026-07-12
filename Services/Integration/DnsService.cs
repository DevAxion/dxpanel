using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Integration;

/// <summary>
/// Manages BIND9 zone files. Zone content is generated from the DnsRecords
/// stored in the panel DB so the on-disk zone always matches the UI.
/// </summary>
public class DnsService : IDnsService
{
    private const string ServiceName = "bind9";
    private readonly ICommandRunner _runner;
    private readonly ApplicationDbContext _db;
    private readonly PanelSettings _settings;

    public DnsService(ICommandRunner runner, ApplicationDbContext db, IOptionsMonitor<PanelSettings> settings)
    {
        _runner = runner;
        _db = db;
        _settings = settings.CurrentValue;
    }

    private string ZonePath(string domain) => $"{_settings.Bind.ZonesDir}/{domain}.zone";

    public async Task<ServiceResult> CreateZoneAsync(string domain)
    {
        var result = new ServiceResult { Message = $"BIND zone created for {domain}." };

        var zone = await BuildZoneFileAsync(domain);
        result.Commands.Add(await _runner.WriteFileAsync(ZonePath(domain), zone, ServiceName));

        // Register the zone in named.conf.local (idempotent append via bash).
        var zoneDecl = $"zone \\\"{domain}\\\" {{ type master; file \\\"{ZonePath(domain)}\\\"; }};";
        result.Commands.Add(await _runner.RunAsync(
            $"grep -q '{domain}' {_settings.Bind.NamedConfLocal} || echo '{zoneDecl}' >> {_settings.Bind.NamedConfLocal}",
            ServiceName));

        var check = await _runner.RunAsync($"named-checkzone {domain} {ZonePath(domain)}", ServiceName);
        result.Commands.Add(check);
        result.Commands.Add(await _runner.RunAsync("rndc reload", ServiceName));
        return result;
    }

    public async Task<ServiceResult> DeleteZoneAsync(string domain)
    {
        var result = new ServiceResult { Message = $"BIND zone removed for {domain}." };
        result.Commands.Add(await _runner.DeleteFileAsync(ZonePath(domain), ServiceName));
        result.Commands.Add(await _runner.RunAsync(
            $"sed -i '/zone \"{domain}\"/,+0d' {_settings.Bind.NamedConfLocal}", ServiceName));
        result.Commands.Add(await _runner.RunAsync("rndc reload", ServiceName));
        return result;
    }

    public async Task<ServiceResult> AddRecordAsync(string domain, DnsRecordType type, string name, string value, int ttl, int? priority = null)
    {
        // Records live in the DB; regenerate the whole zone file for consistency.
        var result = new ServiceResult { Message = $"Record added and zone regenerated for {domain}." };
        var zone = await BuildZoneFileAsync(domain);
        result.Commands.Add(await _runner.WriteFileAsync(ZonePath(domain), zone, ServiceName));
        result.Commands.Add(await _runner.RunAsync("rndc reload", ServiceName));
        return result;
    }

    public async Task<ServiceResult> RemoveRecordAsync(string domain, int recordId)
    {
        var result = new ServiceResult { Message = $"Record removed and zone regenerated for {domain}." };
        var zone = await BuildZoneFileAsync(domain);
        result.Commands.Add(await _runner.WriteFileAsync(ZonePath(domain), zone, ServiceName));
        result.Commands.Add(await _runner.RunAsync("rndc reload", ServiceName));
        return result;
    }

    public async Task<ServiceResult> ReloadBindAsync()
    {
        var cmd = await _runner.RunAsync("rndc reload", ServiceName);
        return new ServiceResult { Success = cmd.Success, Message = "BIND reloaded.", Commands = { cmd } };
    }

    public async Task<ServiceResult> GetZoneRecordsAsync(string domain)
    {
        var cmd = await _runner.RunAsync($"cat {ZonePath(domain)}", ServiceName);
        return new ServiceResult { Success = cmd.Success, Message = cmd.Output, Commands = { cmd } };
    }

    private async Task<string> BuildZoneFileAsync(string domain)
    {
        var records = await _db.DnsZones
            .Where(z => z.Domain != null && z.Domain.DomainName == domain)
            .SelectMany(z => z.Records)
            .ToListAsync();

        var serial = DateTime.UtcNow.ToString("yyyyMMdd") + "01";
        var ns = _settings.Bind.DefaultNs;
        var ip = _settings.Bind.ServerIp;

        var sb = new StringBuilder();
        sb.AppendLine($"$TTL 3600");
        sb.AppendLine($"@   IN  SOA {ns} admin.{domain}. (");
        sb.AppendLine($"            {serial} ; Serial");
        sb.AppendLine($"            3600       ; Refresh");
        sb.AppendLine($"            1800       ; Retry");
        sb.AppendLine($"            1209600    ; Expire");
        sb.AppendLine($"            86400 )    ; Negative Cache TTL");
        sb.AppendLine();
        sb.AppendLine($"; Name servers");
        sb.AppendLine($"@       IN  NS      {ns}");
        sb.AppendLine();
        sb.AppendLine($"; Default A records");
        sb.AppendLine($"@       IN  A       {ip}");
        sb.AppendLine();

        if (records.Count > 0)
        {
            sb.AppendLine("; Panel-managed records");
            foreach (var r in records)
            {
                var nm = string.IsNullOrEmpty(r.Name) || r.Name == "@" ? "@" : r.Name;
                var prio = (r.Type is DnsRecordType.MX or DnsRecordType.SRV) && r.Priority.HasValue
                    ? $"{r.Priority} "
                    : "";
                sb.AppendLine($"{nm,-8}{r.TTL,-6} IN  {r.Type,-6} {prio}{r.Value}");
            }
        }

        return sb.ToString();
    }
}
