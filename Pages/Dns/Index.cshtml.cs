using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Pages.Dns;

public class ZoneRow
{
    public Domain Domain { get; set; } = null!;
    public DnsZone? Zone { get; set; }
    public int RecordCount { get; set; }
    public string? OwnerName { get; set; }
}

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserScopeService _scope;
    private readonly IAuditLogService _audit;
    private readonly IDnsService _dns;

    public IndexModel(ApplicationDbContext db, IUserScopeService scope, IAuditLogService audit, IDnsService dns)
    {
        _db = db;
        _scope = scope;
        _audit = audit;
        _dns = dns;
    }

    public List<ZoneRow> Zones { get; set; } = new();
    public bool ShowOwner { get; set; }

    public async Task OnGetAsync()
    {
        ShowOwner = User.IsInRole(Roles.SuperAdmin) || User.IsInRole(Roles.Reseller);
        var manageable = await _scope.GetManageableUserIdsAsync(User);

        var domains = await _db.Domains.Include(d => d.User)
            .Where(d => manageable.Contains(d.UserId))
            .OrderBy(d => d.DomainName)
            .ToListAsync();

        var zones = await _db.DnsZones
            .Where(z => manageable.Contains(z.UserId))
            .Include(z => z.Records)
            .ToListAsync();

        Zones = domains.Select(d =>
        {
            var zone = zones.FirstOrDefault(z => z.DomainId == d.Id);
            return new ZoneRow
            {
                Domain = d,
                Zone = zone,
                RecordCount = zone?.Records.Count ?? 0,
                OwnerName = d.User?.UserName
            };
        }).ToList();
    }

    public async Task<IActionResult> OnPostCreateZoneAsync(int domainId)
    {
        var domain = await _db.Domains.FindAsync(domainId);
        if (domain == null || !await _scope.CanManageUserAsync(User, domain.UserId))
        {
            TempData["Error"] = "Domain not found or access denied.";
            return RedirectToPage();
        }

        if (await _db.DnsZones.AnyAsync(z => z.DomainId == domainId))
        {
            TempData["Error"] = "A DNS zone already exists for this domain.";
            return RedirectToPage();
        }

        var zone = new DnsZone
        {
            DomainId = domain.Id,
            UserId = domain.UserId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        // Seed sensible default records
        zone.Records.Add(new DnsRecord { Type = DnsRecordType.A, Name = "@", Value = "203.0.113.10", TTL = 3600 });
        zone.Records.Add(new DnsRecord { Type = DnsRecordType.A, Name = "www", Value = "203.0.113.10", TTL = 3600 });
        zone.Records.Add(new DnsRecord { Type = DnsRecordType.MX, Name = "@", Value = $"mail.{domain.DomainName}", TTL = 3600, Priority = 10 });

        _db.DnsZones.Add(zone);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "DnsZone", zone.Id.ToString(), domain.DomainName);

        var dnsResult = await _dns.CreateZoneAsync(domain.DomainName);
        var suffix = dnsResult.Simulated ? " (BIND9 zone simulated)" : " BIND9 zone written & reloaded.";
        TempData["Success"] = $"DNS zone created for '{domain.DomainName}'.{suffix}";
        return RedirectToPage("/Dns/Records", new { domainId = domain.Id });
    }

    public async Task<IActionResult> OnPostDeleteZoneAsync(int zoneId)
    {
        var zone = await _db.DnsZones.Include(z => z.Domain).FirstOrDefaultAsync(z => z.Id == zoneId);
        if (zone == null || !await _scope.CanManageUserAsync(User, zone.UserId))
        {
            TempData["Error"] = "Zone not found or access denied.";
            return RedirectToPage();
        }

        var name = zone.Domain?.DomainName;
        _db.DnsZones.Remove(zone);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "DnsZone", zoneId.ToString(), name);

        if (!string.IsNullOrEmpty(name)) await _dns.DeleteZoneAsync(name);
        TempData["Success"] = $"DNS zone for '{name}' deleted.";
        return RedirectToPage();
    }
}
