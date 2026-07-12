using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Nodes;

namespace SRXPanel.Pages.Admin;

public class LoadBalancerModel : PageModel
{
    private readonly INodeManagerService _nodes;
    private readonly ApplicationDbContext _db;

    public LoadBalancerModel(INodeManagerService nodes, ApplicationDbContext db)
    {
        _nodes = nodes;
        _db = db;
    }

    public List<NodeCapacity> Capacities { get; private set; } = new();
    public List<RebalanceSuggestion> Suggestions { get; private set; } = new();
    public ServerNode? BestNode { get; private set; }
    public LoadBalancerSettings Settings { get; private set; } = new();

    private async Task<LoadBalancerSettings> GetSettingsAsync()
    {
        var settings = await _db.LoadBalancerSettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (settings == null)
        {
            settings = new LoadBalancerSettings { Id = 1 };
            _db.LoadBalancerSettings.Add(settings);
            await _db.SaveChangesAsync();
        }
        return settings;
    }

    public async Task OnGetAsync()
    {
        Capacities = await _nodes.GetFleetCapacityAsync();
        Suggestions = await _nodes.SuggestRebalanceAsync();
        BestNode = await _nodes.GetBestNodeAsync();
        Settings = await GetSettingsAsync();
    }

    public async Task<IActionResult> OnPostSettingsAsync(bool autoBalance, int threshold, bool geoRouting)
    {
        var settings = await GetSettingsAsync();
        settings.AutoBalance = autoBalance;
        settings.CpuThreshold = Math.Clamp(threshold, 50, 95);
        settings.GeoRouting = geoRouting;
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        TempData["Success"] = "Load balancer settings saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostWeightAsync(int nodeId, int weight)
    {
        var node = await _db.ServerNodes.FirstOrDefaultAsync(n => n.Id == nodeId);
        if (node != null)
        {
            node.Weight = Math.Clamp(weight, 0, 1000);
            await _db.SaveChangesAsync();
        }
        TempData["Success"] = "Node weight updated.";
        return RedirectToPage();
    }

    public IActionResult OnPostApply(int domainId, int toNodeId, int fromNodeId) =>
        RedirectToPage("/Admin/Nodes/Migrate", new { domainId, fromNodeId, toNodeId });
}
