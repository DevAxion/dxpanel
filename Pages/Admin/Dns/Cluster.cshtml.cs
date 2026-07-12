using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Nodes;

namespace SRXPanel.Pages.Admin.Dns;

public class ClusterModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly INodeSshService _ssh;

    public ClusterModel(ApplicationDbContext db, INodeSshService ssh)
    {
        _db = db;
        _ssh = ssh;
    }

    public List<ServerNode> NameServers { get; private set; } = new();
    public int ZoneCount { get; private set; }
    public ServerNode? Primary { get; private set; }

    public async Task OnGetAsync()
    {
        NameServers = await _db.ServerNodes
            .Where(n => n.Type == NodeType.Dns && n.IsActive)
            .OrderBy(n => n.Name).ToListAsync();
        Primary = NameServers.FirstOrDefault();
        ZoneCount = await _db.DnsZones.CountAsync();
    }

    public async Task<IActionResult> OnPostSyncAsync()
    {
        var servers = await _db.ServerNodes.Where(n => n.Type == NodeType.Dns && n.IsActive).ToListAsync();
        var primary = servers.FirstOrDefault();
        if (primary == null)
        {
            TempData["Error"] = "No DNS node found.";
            return RedirectToPage();
        }

        // Trigger an AXFR zone transfer from the primary to each secondary.
        foreach (var secondary in servers.Skip(1))
            await _ssh.ExecuteCommandAsync(secondary, $"rndc retransfer . ; systemctl reload named  # AXFR from {primary.IpAddress}");

        TempData["Success"] = $"Zone transfer triggered to {servers.Count - 1} secondary nameserver(s).";
        return RedirectToPage();
    }
}
