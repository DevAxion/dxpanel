using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Pages.Dns;

public class RecordsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserScopeService _scope;
    private readonly IAuditLogService _audit;
    private readonly IDnsService _dns;

    public RecordsModel(ApplicationDbContext db, IUserScopeService scope, IAuditLogService audit, IDnsService dns)
    {
        _db = db;
        _scope = scope;
        _audit = audit;
        _dns = dns;
    }

    [BindProperty(SupportsGet = true)]
    public int DomainId { get; set; }

    [BindProperty]
    public RecordInput Input { get; set; } = new();

    public DnsZone Zone { get; set; } = null!;
    public Domain Domain { get; set; } = null!;
    public List<DnsRecord> Records { get; set; } = new();

    public class RecordInput
    {
        public int? Id { get; set; }

        [Required]
        public DnsRecordType Type { get; set; } = DnsRecordType.A;

        [Required]
        [StringLength(255)]
        public string Name { get; set; } = "@";

        [Required]
        [StringLength(1000)]
        public string Value { get; set; } = string.Empty;

        [Range(60, 604800)]
        public int TTL { get; set; } = 3600;

        [Range(0, 65535)]
        public int? Priority { get; set; }
    }

    private async Task<bool> LoadZoneAsync()
    {
        var zone = await _db.DnsZones
            .Include(z => z.Domain)
            .Include(z => z.Records)
            .FirstOrDefaultAsync(z => z.DomainId == DomainId);

        if (zone == null || !await _scope.CanManageUserAsync(User, zone.UserId))
        {
            return false;
        }

        Zone = zone;
        Domain = zone.Domain!;
        Records = zone.Records.OrderBy(r => r.Type).ThenBy(r => r.Name).ToList();
        return true;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadZoneAsync()) return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!await LoadZoneAsync()) return NotFound();

        var error = DnsValidator.Validate(Input.Type, Input.Name, Input.Value, Input.Priority);
        if (error != null)
        {
            TempData["Error"] = error;
            return RedirectToPage(new { domainId = DomainId });
        }

        if (Input.Id is int id)
        {
            var record = Zone.Records.FirstOrDefault(r => r.Id == id);
            if (record == null)
            {
                TempData["Error"] = "Record not found.";
                return RedirectToPage(new { domainId = DomainId });
            }
            record.Type = Input.Type;
            record.Name = Input.Name.Trim();
            record.Value = Input.Value.Trim();
            record.TTL = Input.TTL;
            record.Priority = (Input.Type is DnsRecordType.MX or DnsRecordType.SRV) ? Input.Priority : null;
            await _db.SaveChangesAsync();
            await _audit.LogAsync("Update", "DnsRecord", id.ToString(), $"{Input.Type} {Input.Name}");
            TempData["Success"] = "DNS record updated.";
        }
        else
        {
            var record = new DnsRecord
            {
                ZoneId = Zone.Id,
                Type = Input.Type,
                Name = Input.Name.Trim(),
                Value = Input.Value.Trim(),
                TTL = Input.TTL,
                Priority = (Input.Type is DnsRecordType.MX or DnsRecordType.SRV) ? Input.Priority : null,
                CreatedAt = DateTime.UtcNow
            };
            _db.DnsRecords.Add(record);
            await _db.SaveChangesAsync();
            await _audit.LogAsync("Create", "DnsRecord", record.Id.ToString(), $"{Input.Type} {Input.Name}");
            TempData["Success"] = "DNS record added.";
        }

        // Regenerate the BIND zone file from the DB (simulated on Windows/dev).
        await _dns.AddRecordAsync(Domain.DomainName, Input.Type, Input.Name, Input.Value, Input.TTL, Input.Priority);
        return RedirectToPage(new { domainId = DomainId });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int recordId)
    {
        if (!await LoadZoneAsync()) return NotFound();

        var record = Zone.Records.FirstOrDefault(r => r.Id == recordId);
        if (record == null)
        {
            TempData["Error"] = "Record not found.";
            return RedirectToPage(new { domainId = DomainId });
        }

        _db.DnsRecords.Remove(record);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "DnsRecord", recordId.ToString(), $"{record.Type} {record.Name}");

        await _dns.RemoveRecordAsync(Domain.DomainName, recordId);
        TempData["Success"] = "DNS record deleted.";
        return RedirectToPage(new { domainId = DomainId });
    }

    public async Task<IActionResult> OnPostTemplateAsync(string template)
    {
        if (!await LoadZoneAsync()) return NotFound();

        var toAdd = new List<DnsRecord>();
        switch (template)
        {
            case "mail":
                toAdd.Add(new DnsRecord { ZoneId = Zone.Id, Type = DnsRecordType.MX, Name = "@", Value = $"mail.{Domain.DomainName}", TTL = 3600, Priority = 10 });
                toAdd.Add(new DnsRecord { ZoneId = Zone.Id, Type = DnsRecordType.A, Name = "mail", Value = "203.0.113.10", TTL = 3600 });
                toAdd.Add(new DnsRecord { ZoneId = Zone.Id, Type = DnsRecordType.TXT, Name = "@", Value = "v=spf1 a mx ~all", TTL = 3600 });
                break;
            case "www":
                toAdd.Add(new DnsRecord { ZoneId = Zone.Id, Type = DnsRecordType.A, Name = "@", Value = "203.0.113.10", TTL = 3600 });
                toAdd.Add(new DnsRecord { ZoneId = Zone.Id, Type = DnsRecordType.CNAME, Name = "www", Value = $"{Domain.DomainName}.", TTL = 3600 });
                break;
            case "ftp":
                toAdd.Add(new DnsRecord { ZoneId = Zone.Id, Type = DnsRecordType.A, Name = "ftp", Value = "203.0.113.10", TTL = 3600 });
                break;
            default:
                TempData["Error"] = "Unknown template.";
                return RedirectToPage(new { domainId = DomainId });
        }

        // Skip records that would duplicate an existing (Type, Name)
        foreach (var r in toAdd)
        {
            if (!Zone.Records.Any(x => x.Type == r.Type && x.Name == r.Name && x.Value == r.Value))
            {
                _db.DnsRecords.Add(r);
            }
        }
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Template", "DnsRecord", Zone.Id.ToString(), $"{template} preset for {Domain.DomainName}");

        await _dns.ReloadBindAsync();
        await _dns.AddRecordAsync(Domain.DomainName, DnsRecordType.A, "@", "203.0.113.10", 3600);
        TempData["Success"] = $"Applied '{template}' template.";
        return RedirectToPage(new { domainId = DomainId });
    }
}
