using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Nodes;

namespace SRXPanel.Pages.Admin.Nodes;

public class MigrateModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly INodeManagerService _nodes;

    public MigrateModel(ApplicationDbContext db, INodeManagerService nodes)
    {
        _db = db;
        _nodes = nodes;
    }

    [BindProperty(SupportsGet = true)] public int? DomainId { get; set; }
    [BindProperty(SupportsGet = true)] public int? FromNodeId { get; set; }
    [BindProperty(SupportsGet = true)] public int? ToNodeId { get; set; }

    public List<ServerNode> AllNodes { get; private set; } = new();
    public List<Domain> Domains { get; private set; } = new();

    public Domain? SelectedDomain { get; private set; }
    public ServerNode? SourceNode { get; private set; }
    public ServerNode? TargetNode { get; private set; }
    public MigrationPreflight? Preflight { get; private set; }
    public MigrationType Type { get; private set; } = MigrationType.Full;

    /// <summary>Set once a migration has been kicked off — the page then streams progress.</summary>
    public int? MigrationId { get; private set; }

    public async Task OnGetAsync()
    {
        AllNodes = await _db.ServerNodes.Where(n => n.IsActive).OrderBy(n => n.Name).ToListAsync();

        // Domains that already have a node placement, so we know their source.
        Domains = await _db.Domains
            .Where(d => _db.DomainNodes.Any(dn => dn.DomainId == d.Id))
            .OrderBy(d => d.DomainName).ToListAsync();

        if (DomainId is int domainId)
        {
            SelectedDomain = await _db.Domains.FirstOrDefaultAsync(d => d.Id == domainId);
            var placement = await _db.DomainNodes.FirstOrDefaultAsync(dn => dn.DomainId == domainId);
            FromNodeId ??= placement?.NodeId;
        }

        if (FromNodeId is int f) SourceNode = AllNodes.FirstOrDefault(n => n.Id == f);
        if (ToNodeId is int t) TargetNode = AllNodes.FirstOrDefault(n => n.Id == t);
    }

    public async Task<IActionResult> OnPostPreflightAsync(int domainId, int fromNodeId, int toNodeId, MigrationType type)
    {
        await OnGetAsync();
        DomainId = domainId; FromNodeId = fromNodeId; ToNodeId = toNodeId; Type = type;

        SelectedDomain = await _db.Domains.FirstOrDefaultAsync(d => d.Id == domainId);
        SourceNode = AllNodes.FirstOrDefault(n => n.Id == fromNodeId);
        TargetNode = AllNodes.FirstOrDefault(n => n.Id == toNodeId);

        if (fromNodeId == toNodeId)
        {
            TempData["Error"] = "Source and target must be different nodes.";
            return Page();
        }

        Preflight = await _nodes.PreflightAsync(domainId, fromNodeId, toNodeId, type);
        return Page();
    }

    public async Task<IActionResult> OnPostStartAsync(int domainId, int fromNodeId, int toNodeId, MigrationType type)
    {
        await OnGetAsync();
        DomainId = domainId; FromNodeId = fromNodeId; ToNodeId = toNodeId; Type = type;

        SelectedDomain = await _db.Domains.FirstOrDefaultAsync(d => d.Id == domainId);
        SourceNode = AllNodes.FirstOrDefault(n => n.Id == fromNodeId);
        TargetNode = AllNodes.FirstOrDefault(n => n.Id == toNodeId);
        Preflight = await _nodes.PreflightAsync(domainId, fromNodeId, toNodeId, type);

        if (!Preflight.CanProceed)
        {
            TempData["Error"] = "Pre-flight check failed — resolve the blocking issues first.";
            return Page();
        }

        MigrationId = _nodes.StartMigration(domainId, fromNodeId, toNodeId, type, User.Identity?.Name ?? "admin");
        return Page();
    }
}
