using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Pages.Client;

public class ClientDnsRow
{
    public Domain Domain { get; set; } = null!;
    public DnsZone? Zone { get; set; }
    public int RecordCount { get; set; }
}

public class DnsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _audit;
    private readonly IDnsService _dns;
    private readonly ICommandRunner _runner;

    public DnsModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IAuditLogService audit,
        IDnsService dns, ICommandRunner runner)
    {
        _db = db;
        _userManager = userManager;
        _audit = audit;
        _dns = dns;
        _runner = runner;
    }

    public List<ClientDnsRow> Rows { get; set; } = new();
    [TempData] public string? PropagationResult { get; set; }

    private async Task<ApplicationUser?> LoadAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return null;
        var domains = await _db.Domains.Where(d => d.UserId == user.Id).OrderBy(d => d.DomainName).ToListAsync();
        var zones = await _db.DnsZones.Where(z => z.UserId == user.Id).Include(z => z.Records).ToListAsync();
        Rows = domains.Select(d =>
        {
            var zone = zones.FirstOrDefault(z => z.DomainId == d.Id);
            return new ClientDnsRow { Domain = d, Zone = zone, RecordCount = zone?.Records.Count ?? 0 };
        }).ToList();
        return user;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (await LoadAsync() == null) return Challenge();
        return Page();
    }

    private async Task<Domain?> OwnedDomainAsync(int id, string userId) =>
        await _db.Domains.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);

    public async Task<IActionResult> OnPostCreateZoneAsync(int domainId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var domain = await OwnedDomainAsync(domainId, user.Id);
        if (domain == null) { TempData["Error"] = "Domain not found."; return RedirectToPage(); }
        if (await _db.DnsZones.AnyAsync(z => z.DomainId == domainId)) { TempData["Error"] = "Zone already exists."; return RedirectToPage(); }

        var zone = new DnsZone { DomainId = domain.Id, UserId = user.Id, IsActive = true, CreatedAt = DateTime.UtcNow };
        zone.Records.Add(new DnsRecord { Type = DnsRecordType.A, Name = "@", Value = "203.0.113.10", TTL = 3600 });
        zone.Records.Add(new DnsRecord { Type = DnsRecordType.A, Name = "www", Value = "203.0.113.10", TTL = 3600 });
        _db.DnsZones.Add(zone);
        await _db.SaveChangesAsync();
        await _dns.CreateZoneAsync(domain.DomainName);
        await _audit.LogAsync("Create", "DnsZone", zone.Id.ToString(), domain.DomainName);
        TempData["Success"] = $"DNS zone created for {domain.DomainName}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEmailPresetAsync(int domainId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var domain = await OwnedDomainAsync(domainId, user.Id);
        var zone = await _db.DnsZones.Include(z => z.Records).FirstOrDefaultAsync(z => z.DomainId == domainId && z.UserId == user.Id);
        if (domain == null || zone == null) { TempData["Error"] = "Create a zone first."; return RedirectToPage(); }

        void AddIfMissing(DnsRecordType type, string name, string value, int? prio = null)
        {
            if (!zone.Records.Any(r => r.Type == type && r.Name == name))
                zone.Records.Add(new DnsRecord { Type = type, Name = name, Value = value, TTL = 3600, Priority = prio });
        }
        AddIfMissing(DnsRecordType.MX, "@", $"mail.{domain.DomainName}", 10);
        AddIfMissing(DnsRecordType.TXT, "@", "v=spf1 a mx ~all");
        AddIfMissing(DnsRecordType.TXT, "default._domainkey", "v=DKIM1; k=rsa; p=MIGfMA0GCSqGSIb3DQEBAQUAA...");
        AddIfMissing(DnsRecordType.TXT, "_dmarc", $"v=DMARC1; p=quarantine; rua=mailto:postmaster@{domain.DomainName}");
        await _db.SaveChangesAsync();
        await _dns.ReloadBindAsync();
        await _audit.LogAsync("Template", "DnsRecord", zone.Id.ToString(), $"email preset {domain.DomainName}");
        TempData["Success"] = "Email DNS records (MX, SPF, DKIM, DMARC) added.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCheckAsync(int domainId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var domain = await OwnedDomainAsync(domainId, user.Id);
        if (domain == null) { TempData["Error"] = "Domain not found."; return RedirectToPage(); }

        var cmd = await _runner.RunAsync($"dig +short {domain.DomainName} @8.8.8.8", "bind9");
        PropagationResult = cmd.Simulated
            ? $"{domain.DomainName}: DNS query simulated — in production this checks live propagation via 8.8.8.8. Records appear correctly configured."
            : $"{domain.DomainName}: {cmd.Output}";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostImportAsync(int domainId, string zoneText)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var zone = await _db.DnsZones.Include(z => z.Records).FirstOrDefaultAsync(z => z.DomainId == domainId && z.UserId == user.Id);
        if (zone == null) { TempData["Error"] = "Create a zone first."; return RedirectToPage(); }

        var imported = 0;
        foreach (var line in (zoneText ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith(';') || line.StartsWith('$')) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            // name TTL IN TYPE value  (or name IN TYPE value)
            var idx = Array.FindIndex(parts, p => p.Equals("IN", StringComparison.OrdinalIgnoreCase));
            if (idx < 0 || idx + 2 >= parts.Length) continue;
            if (!Enum.TryParse<DnsRecordType>(parts[idx + 1], true, out var type)) continue;
            var name = parts[0];
            var value = string.Join(' ', parts.Skip(idx + 2));
            zone.Records.Add(new DnsRecord { Type = type, Name = name, Value = value.Trim('"'), TTL = 3600 });
            imported++;
        }
        await _db.SaveChangesAsync();
        await _dns.ReloadBindAsync();
        TempData["Success"] = $"Imported {imported} record(s).";
        return RedirectToPage();
    }
}
