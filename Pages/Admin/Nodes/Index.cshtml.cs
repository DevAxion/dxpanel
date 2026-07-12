using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services.Nodes;

namespace SRXPanel.Pages.Admin.Nodes;

public class IndexModel : PageModel
{
    private readonly INodeManagerService _nodes;
    private readonly INodeSshService _ssh;

    public IndexModel(INodeManagerService nodes, INodeSshService ssh)
    {
        _nodes = nodes;
        _ssh = ssh;
    }

    public record NodeRow(ServerNode Node, ServerMetric? Metric, int DomainCount, int UserCount);

    public List<NodeRow> Nodes { get; private set; } = new();

    public int OnlineCount => Nodes.Count(n => n.Node.Status == NodeStatus.Online);
    public int TotalCount => Nodes.Count;
    public int UnackedAlerts { get; set; }

    public async Task OnGetAsync()
    {
        var nodes = await _nodes.GetAllNodesAsync();
        foreach (var node in nodes)
        {
            Nodes.Add(new NodeRow(node,
                await _nodes.GetLatestMetricAsync(node.Id),
                await _nodes.DomainCountAsync(node.Id),
                await _nodes.UserCountAsync(node.Id)));
        }
    }

    public async Task<IActionResult> OnPostRefreshAsync()
    {
        // Ping each node so the list reflects current reachability immediately.
        var nodes = await _nodes.GetAllNodesAsync();
        foreach (var node in nodes)
            await _ssh.TestConnectionAsync(node);

        TempData["Success"] = $"Pinged {nodes.Count} node(s).";
        return RedirectToPage();
    }
}
