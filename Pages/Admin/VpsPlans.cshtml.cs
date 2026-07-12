using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;

namespace SRXPanel.Pages.Admin;

public class VpsPlansModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditLogService _audit;

    public VpsPlansModel(ApplicationDbContext db, IAuditLogService audit)
    {
        _db = db;
        _audit = audit;
    }

    public List<VpsPlan> Plans { get; set; } = new();
    public List<ProxmoxNode> Nodes { get; set; } = new();
    public List<VpsTemplate> Templates { get; set; } = new();

    /// <summary>Distinct OS slugs offered by the configured VPS templates.</summary>
    public List<string> AvailableOs { get; set; } = new();

    /// <summary>Distinct locations from the configured Proxmox nodes (datalist suggestions).</summary>
    public List<string> KnownLocations { get; set; } = new();

    /// <summary>Instance counts per plan id, used to gate deletion.</summary>
    public Dictionary<int, int> InstanceCounts { get; set; } = new();

    /// <summary>Set when the inline form is editing an existing plan.</summary>
    public bool EditMode { get; set; }

    [BindProperty]
    public VpsPlanInput Input { get; set; } = new();

    /// <summary>OS slugs ticked in the form; persisted to VpsPlan.OsOptions as CSV.</summary>
    [BindProperty]
    public List<string> SelectedOs { get; set; } = new();

    /// <summary>Template ids selected in the form; persisted to VpsPlan.TemplateIds as CSV.</summary>
    [BindProperty]
    public List<int> SelectedTemplateIds { get; set; } = new();

    public class VpsPlanInput
    {
        public int Id { get; set; }

        [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
        [StringLength(300)] public string? Description { get; set; }

        [Range(1, 512)] public int CpuCores { get; set; } = 1;
        [Range(128, 1048576)] public int RamMB { get; set; } = 1024;
        [Range(1, 1048576)] public int DiskGB { get; set; } = 20;
        [Range(0, 10485760)] public int BandwidthGB { get; set; } = 1000;

        [Range(0, 100000)] public decimal Price { get; set; }
        public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;

        [StringLength(80)] public string Location { get; set; } = "Frankfurt, DE";

        public int? NodeId { get; set; }

        public bool IsPopular { get; set; }
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
    }

    public async Task OnGetAsync(int? editId)
    {
        await LoadListsAsync();

        if (editId is > 0)
        {
            var plan = await _db.VpsPlans.FirstOrDefaultAsync(p => p.Id == editId);
            if (plan != null)
            {
                EditMode = true;
                Input = new VpsPlanInput
                {
                    Id = plan.Id,
                    Name = plan.Name,
                    Description = plan.Description,
                    CpuCores = plan.CpuCores,
                    RamMB = plan.RamMB,
                    DiskGB = plan.DiskGB,
                    BandwidthGB = plan.BandwidthGB,
                    Price = plan.Price,
                    BillingCycle = plan.BillingCycle,
                    Location = plan.Location,
                    NodeId = plan.NodeId,
                    IsPopular = plan.IsPopular,
                    IsActive = plan.IsActive,
                    SortOrder = plan.SortOrder
                };
                SelectedOs = plan.OsList.ToList();
                SelectedTemplateIds = plan.TemplateIdList.ToList();
            }
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadListsAsync();
            EditMode = Input.Id > 0;
            return Page();
        }

        VpsPlan plan;
        if (Input.Id > 0)
        {
            plan = await _db.VpsPlans.FindAsync(Input.Id) ?? new VpsPlan();
            if (plan.Id == 0) { TempData["Error"] = "VPS plan not found."; return RedirectToPage(); }
        }
        else
        {
            plan = new VpsPlan();
            _db.VpsPlans.Add(plan);
        }

        plan.Name = Input.Name;
        plan.Description = Input.Description;
        plan.CpuCores = Input.CpuCores;
        plan.RamMB = Input.RamMB;
        plan.DiskGB = Input.DiskGB;
        plan.BandwidthGB = Input.BandwidthGB;
        plan.Price = Input.Price;
        plan.BillingCycle = Input.BillingCycle;
        plan.Location = Input.Location;
        plan.NodeId = Input.NodeId;
        plan.IsPopular = Input.IsPopular;
        plan.IsActive = Input.IsActive;
        plan.SortOrder = Input.SortOrder;

        // OsOptions / TemplateIds are stored as CSV per the model's OsList / TemplateIdList helpers.
        plan.OsOptions = string.Join(",", SelectedOs.Where(s => !string.IsNullOrWhiteSpace(s)));
        plan.TemplateIds = string.Join(",", SelectedTemplateIds.Distinct());

        await _db.SaveChangesAsync();
        await _audit.LogAsync(Input.Id > 0 ? "Update" : "Create", "VpsPlan", plan.Id.ToString(), plan.Name);
        TempData["Success"] = $"VPS plan '{plan.Name}' saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var plan = await _db.VpsPlans.FindAsync(id);
        if (plan == null) { TempData["Error"] = "VPS plan not found."; return RedirectToPage(); }
        plan.IsActive = !plan.IsActive;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(plan.IsActive ? "Activate" : "Deactivate", "VpsPlan", plan.Id.ToString(), plan.Name);
        TempData["Success"] = $"VPS plan '{plan.Name}' {(plan.IsActive ? "activated" : "deactivated")}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var plan = await _db.VpsPlans.FindAsync(id);
        if (plan == null) { TempData["Error"] = "VPS plan not found."; return RedirectToPage(); }

        if (await _db.VpsInstances.AnyAsync(v => v.PlanId == id))
        {
            TempData["Error"] = $"Cannot delete '{plan.Name}' — VPS instances still reference it. Deactivate it instead.";
            return RedirectToPage();
        }

        _db.VpsPlans.Remove(plan);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "VpsPlan", id.ToString(), plan.Name);
        TempData["Success"] = $"VPS plan '{plan.Name}' deleted.";
        return RedirectToPage();
    }

    private async Task LoadListsAsync()
    {
        Plans = (await _db.VpsPlans.ToListAsync())
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Price).ToList();

        Nodes = await _db.ProxmoxNodes.OrderBy(n => n.Name).ToListAsync();
        Templates = await _db.VpsTemplates.Include(t => t.Node)
            .OrderBy(t => t.Name).ToListAsync();

        AvailableOs = Templates
            .Select(t => t.OsType.Trim().ToLowerInvariant())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .OrderBy(s => s)
            .ToList();

        KnownLocations = Nodes
            .Select(n => n.Location)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct()
            .OrderBy(l => l)
            .ToList();

        // How many instances reference each plan (drives the delete/deactivate choice in the UI).
        InstanceCounts = await _db.VpsInstances
            .GroupBy(v => v.PlanId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
    }
}
