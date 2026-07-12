using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Vps;

namespace SRXPanel.Pages.Admin;

public class ProxmoxModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IProxmoxService _proxmox;
    private readonly IAuditLogService _auditLog;

    public ProxmoxModel(ApplicationDbContext db, IProxmoxService proxmox, IAuditLogService auditLog)
    {
        _db = db;
        _proxmox = proxmox;
        _auditLog = auditLog;
    }

    public List<ProxmoxNode> Nodes { get; private set; } = new();
    public Dictionary<int, ProxmoxNodeStatus> Status { get; private set; } = new();
    public List<VpsTemplate> Templates { get; private set; } = new();
    public Dictionary<int, (int total, int used)> IpPool { get; private set; } = new();
    public List<VpsIpAddress> Addresses { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Nodes = await _db.ProxmoxNodes.OrderBy(n => n.Name).ToListAsync();
        Templates = await _db.VpsTemplates.Include(t => t.Node).OrderBy(t => t.NodeId).ThenBy(t => t.Name).ToListAsync();
        Addresses = await _db.VpsIpAddresses.OrderBy(a => a.NodeId).ThenBy(a => a.Id).ToListAsync();

        foreach (var node in Nodes.Where(n => n.IsActive))
            Status[node.Id] = await _proxmox.GetNodeStatusAsync(node);

        foreach (var node in Nodes)
        {
            var total = Addresses.Count(a => a.NodeId == node.Id);
            var used = Addresses.Count(a => a.NodeId == node.Id && a.AssignedInstanceId != null);
            IpPool[node.Id] = (total, used);
        }
    }

    // ---------------- Nodes ----------------

    public async Task<IActionResult> OnPostAddNodeAsync(string name, string host, int port, string username,
        string tokenId, string? tokenSecret, bool verifySsl, int maxVms, string storage, string network, string location)
    {
        _db.ProxmoxNodes.Add(new ProxmoxNode
        {
            Name = name.Trim(), Host = host.Trim(), Port = port <= 0 ? 8006 : port,
            Username = string.IsNullOrWhiteSpace(username) ? "root@pam" : username.Trim(),
            TokenId = tokenId?.Trim() ?? "", TokenSecret = string.IsNullOrWhiteSpace(tokenSecret) ? "sim_token" : tokenSecret.Trim(),
            VerifySsl = verifySsl, MaxVms = maxVms <= 0 ? 100 : maxVms,
            Storage = string.IsNullOrWhiteSpace(storage) ? "local-lvm" : storage.Trim(),
            Network = string.IsNullOrWhiteSpace(network) ? "vmbr0" : network.Trim(),
            Location = string.IsNullOrWhiteSpace(location) ? "Frankfurt, DE" : location.Trim()
        });
        await _db.SaveChangesAsync();
        await _auditLog.LogAsync("AddNode", "ProxmoxNode", "", name);
        TempData["Success"] = $"Node {name} added.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEditNodeAsync(int id, string name, string host, int port, int maxVms,
        string storage, string network, string location, bool isActive)
    {
        var node = await _db.ProxmoxNodes.FindAsync(id);
        if (node == null) return NotFound();
        node.Name = name.Trim(); node.Host = host.Trim(); node.Port = port; node.MaxVms = maxVms;
        node.Storage = storage.Trim(); node.Network = network.Trim(); node.Location = location.Trim(); node.IsActive = isActive;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Node updated.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteNodeAsync(int id)
    {
        var node = await _db.ProxmoxNodes.FindAsync(id);
        if (node == null) return NotFound();
        if (await _db.VpsInstances.AnyAsync(v => v.NodeId == id && v.Status != VpsStatus.Deleted))
        {
            TempData["Error"] = "Move or delete the VPS on this node first.";
            return RedirectToPage();
        }
        _db.ProxmoxNodes.Remove(node);
        await _db.SaveChangesAsync();
        await _auditLog.LogAsync("DeleteNode", "ProxmoxNode", id.ToString(), node.Name);
        TempData["Success"] = "Node removed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestNodeAsync(int id)
    {
        var node = await _db.ProxmoxNodes.FindAsync(id);
        if (node == null) return NotFound();
        var (ok, latency) = await _proxmox.TestConnectionAsync(node);
        if (ok) { node.LastSeenAt = DateTime.UtcNow; await _db.SaveChangesAsync(); }
        TempData[ok ? "Success" : "Error"] = ok ? $"{node.Name}: connection OK ({latency} ms)." : $"{node.Name}: connection failed.";
        return RedirectToPage();
    }

    // ---------------- Templates ----------------

    public async Task<IActionResult> OnPostAddTemplateAsync(int nodeId, string name, string osType, int templateId,
        string? description, int minDiskGB, int minRamMB, int minCpuCores)
    {
        _db.VpsTemplates.Add(new VpsTemplate
        {
            NodeId = nodeId, Name = name.Trim(), OsType = osType.Trim().ToLowerInvariant(), TemplateId = templateId,
            Description = description, MinDiskGB = Math.Max(1, minDiskGB), MinRamMB = Math.Max(256, minRamMB),
            MinCpuCores = Math.Max(1, minCpuCores)
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Template {name} added.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleTemplateAsync(int id)
    {
        var t = await _db.VpsTemplates.FindAsync(id);
        if (t == null) return NotFound();
        t.IsActive = !t.IsActive;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Template {(t.IsActive ? "enabled" : "disabled")}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteTemplateAsync(int id)
    {
        var t = await _db.VpsTemplates.FindAsync(id);
        if (t == null) return NotFound();
        _db.VpsTemplates.Remove(t);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Template deleted.";
        return RedirectToPage();
    }

    // ---------------- IP pool ----------------

    public async Task<IActionResult> OnPostAddIpRangeAsync(int nodeId, string startIp, int count, string? gateway, int prefix)
    {
        if (!IPAddress.TryParse(startIp, out var start) || start.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            TempData["Error"] = "Enter a valid IPv4 start address.";
            return RedirectToPage();
        }

        var bytes = start.GetAddressBytes();
        var added = 0;
        count = Math.Clamp(count, 1, 256);
        for (var i = 0; i < count; i++)
        {
            var addr = new IPAddress(bytes).ToString();
            if (!await _db.VpsIpAddresses.AnyAsync(a => a.Address == addr))
            {
                _db.VpsIpAddresses.Add(new VpsIpAddress
                {
                    NodeId = nodeId, Address = addr, Gateway = gateway, Prefix = prefix <= 0 ? 24 : prefix
                });
                added++;
            }
            // increment last octet
            for (var b = 3; b >= 0; b--) { if (++bytes[b] != 0) break; }
        }
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Added {added} address(es) to the pool.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteIpAsync(int id)
    {
        var ip = await _db.VpsIpAddresses.FindAsync(id);
        if (ip == null) return NotFound();
        if (ip.AssignedInstanceId != null) { TempData["Error"] = "Address is assigned — release it first."; return RedirectToPage(); }
        _db.VpsIpAddresses.Remove(ip);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Address removed from the pool.";
        return RedirectToPage();
    }
}
