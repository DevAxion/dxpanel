using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Vps;

namespace SRXPanel.Pages.Client.Vps;

public class OrderModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IVpsManagerService _vps;
    private readonly IVpsProvisioningService _provisioning;
    private readonly IAuditLogService _auditLog;

    public OrderModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IVpsManagerService vps,
        IVpsProvisioningService provisioning, IAuditLogService auditLog)
    {
        _db = db;
        _userManager = userManager;
        _vps = vps;
        _provisioning = provisioning;
        _auditLog = auditLog;
    }

    [BindProperty(SupportsGet = true)] public int PlanId { get; set; }

    public VpsPlan Plan { get; private set; } = null!;
    public List<VpsTemplate> Templates { get; private set; } = new();
    public List<ProxmoxNode> Nodes { get; private set; } = new();
    public List<SshKey> SshKeys { get; private set; } = new();

    private async Task<bool> LoadAsync()
    {
        var plan = await _db.VpsPlans.FirstOrDefaultAsync(p => p.Id == PlanId && p.IsActive);
        if (plan == null) return false;
        Plan = plan;

        Nodes = await _db.ProxmoxNodes.Where(n => n.IsActive).ToListAsync();
        var nodeIds = Nodes.Select(n => n.Id).ToList();

        var templatesQuery = _db.VpsTemplates.Where(t => t.IsActive && nodeIds.Contains(t.NodeId));
        var allowed = plan.TemplateIdList.ToList();
        if (allowed.Count > 0) templatesQuery = templatesQuery.Where(t => allowed.Contains(t.Id));
        Templates = await templatesQuery.ToListAsync();

        var userId = _userManager.GetUserId(User)!;
        SshKeys = await _db.SshKeys.Where(k => k.UserId == userId).ToListAsync();
        return true;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync()) return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string hostname, string? rootPassword, int? sshKeyId, int templateId, int? nodeId)
    {
        if (!await LoadAsync()) return NotFound();
        var userId = _userManager.GetUserId(User)!;

        hostname = (hostname ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(hostname) || !System.Text.RegularExpressions.Regex.IsMatch(hostname, @"^[a-z0-9]([a-z0-9\-\.]{1,61}[a-z0-9])?$"))
        {
            TempData["Error"] = "Enter a valid hostname (letters, digits, hyphens and dots).";
            return Page();
        }

        var hasKey = sshKeyId.HasValue && SshKeys.Any(k => k.Id == sshKeyId.Value);
        if (!hasKey && string.IsNullOrWhiteSpace(rootPassword))
        {
            TempData["Error"] = "Provide a root password or select an SSH key.";
            return Page();
        }
        if (!hasKey && rootPassword!.Length < 8)
        {
            TempData["Error"] = "Root password must be at least 8 characters.";
            return Page();
        }

        if (!Templates.Any(t => t.Id == templateId))
            templateId = Templates.FirstOrDefault()?.Id ?? 0;

        var vps = await _vps.CreateInstanceAsync(userId, Plan,
            new VpsOrderConfig(hostname, hasKey ? null : rootPassword, sshKeyId, templateId, nodeId ?? Plan.NodeId));

        // Record the purchase as a ClientService so it appears under "My Services" and billing.
        _db.ClientServices.Add(new ClientService
        {
            UserId = userId,
            Type = ClientServiceType.Vps,
            ReferenceId = vps.Id,
            Name = $"{Plan.Name} — {hostname}",
            ResourceSummary = $"{Plan.CpuCores} vCPU · {Plan.RamMB / 1024} GB RAM · {Plan.DiskGB} GB · {Plan.BandwidthGB / 1000} TB",
            Price = Plan.Price,
            BillingCycle = Plan.BillingCycle,
            Status = SubscriptionStatus.Active,
            CurrentPeriodStart = DateTime.UtcNow,
            CurrentPeriodEnd = vps.ExpiresAt ?? DateTime.UtcNow.AddMonths(1)
        });
        await _db.SaveChangesAsync();

        _provisioning.StartProvisioning(vps.Id);
        await _auditLog.LogAsync("Order", "VpsInstance", vps.Id.ToString(), $"{Plan.Name} / {hostname}");

        TempData["Success"] = "Your VPS is being deployed.";
        return RedirectToPage("/Client/Vps/Detail", new { id = vps.Id });
    }
}
