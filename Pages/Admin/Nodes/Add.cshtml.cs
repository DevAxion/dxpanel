using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Nodes;

namespace SRXPanel.Pages.Admin.Nodes;

public class AddModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly INodeSshService _ssh;
    private readonly IAuditLogService _auditLog;

    public AddModel(ApplicationDbContext db, INodeSshService ssh, IAuditLogService auditLog)
    {
        _db = db;
        _ssh = ssh;
        _auditLog = auditLog;
    }

    public void OnGet() { }

    /// <summary>Tests SSH connectivity and auto-detects installed services for an unsaved node.</summary>
    public async Task<IActionResult> OnPostTestAsync(string hostname, string ipAddress, int sshPort,
        string sshUsername, string? sshKeyPath, string? sshPassword)
    {
        var probe = new ServerNode
        {
            Name = "probe", Hostname = hostname, IpAddress = ipAddress, SshPort = sshPort <= 0 ? 22 : sshPort,
            SshUsername = string.IsNullOrWhiteSpace(sshUsername) ? "root" : sshUsername,
            SshKeyPath = sshKeyPath, SshPassword = sshPassword
        };

        var (ok, latency) = await _ssh.TestConnectionAsync(probe);
        if (!ok)
            return new JsonResult(new { ok = false, message = "Could not connect. Check the IP, port and credentials." });

        // Detect which services are installed/running.
        var detected = new List<object>();
        foreach (var type in Enum.GetValues<ServerServiceType>())
        {
            var status = await _ssh.GetServiceStatusAsync(probe, type);
            if (status != ServerServiceStatus.NotInstalled)
                detected.Add(new { service = type.ToString(), status = status.ToString() });
        }

        var uname = await _ssh.ExecuteCommandAsync(probe, "uname -a");
        return new JsonResult(new { ok = true, latency, os = uname.Output.Trim(), services = detected });
    }

    public async Task<IActionResult> OnPostAsync(string name, string hostname, string ipAddress, int sshPort,
        string sshUsername, string? sshKeyPath, string? sshPassword, NodeType type, string location,
        int cpuCores, int ramGB, int diskGB, int weight)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(ipAddress))
        {
            TempData["Error"] = "Name and IP address are required.";
            return Page();
        }

        if (_db.ServerNodes.Any(n => n.Name == name))
        {
            TempData["Error"] = $"A node named '{name}' already exists.";
            return Page();
        }

        var node = new ServerNode
        {
            Name = name.Trim(),
            Hostname = string.IsNullOrWhiteSpace(hostname) ? ipAddress : hostname.Trim(),
            IpAddress = ipAddress.Trim(),
            SshPort = sshPort <= 0 ? 22 : sshPort,
            SshUsername = string.IsNullOrWhiteSpace(sshUsername) ? "root" : sshUsername.Trim(),
            SshKeyPath = string.IsNullOrWhiteSpace(sshKeyPath) ? null : sshKeyPath.Trim(),
            SshPassword = string.IsNullOrWhiteSpace(sshPassword) ? null : sshPassword,
            Type = type,
            Location = string.IsNullOrWhiteSpace(location) ? "Unknown" : location.Trim(),
            CpuCores = cpuCores, RamGB = ramGB, DiskGB = diskGB, Weight = weight <= 0 ? 100 : weight,
            IsActive = true, CreatedAt = DateTime.UtcNow
        };

        // Verify + detect on save so the node lands with an accurate status and service list.
        var (ok, latency) = await _ssh.TestConnectionAsync(node);
        node.Status = ok ? NodeStatus.Online : NodeStatus.Offline;
        node.LatencyMs = ok ? latency : null;
        node.LastPingAt = DateTime.UtcNow;

        _db.ServerNodes.Add(node);
        await _db.SaveChangesAsync();

        if (ok)
        {
            foreach (var serviceType in Enum.GetValues<ServerServiceType>())
            {
                var status = await _ssh.GetServiceStatusAsync(node, serviceType);
                if (status != ServerServiceStatus.NotInstalled)
                    _db.ServerServices.Add(new ServerService
                    {
                        NodeId = node.Id, ServiceType = serviceType, Status = status, LastCheckedAt = DateTime.UtcNow
                    });
            }
            await _db.SaveChangesAsync();
        }

        await _auditLog.LogAsync("Create", "ServerNode", node.Id.ToString(), node.Name);
        TempData["Success"] = ok
            ? $"Node '{node.Name}' added and online ({latency} ms). Agent install command shown on the node page."
            : $"Node '{node.Name}' saved but could not be reached — check connectivity.";
        return RedirectToPage("/Admin/Nodes/Detail", new { id = node.Id });
    }
}
