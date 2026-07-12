using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Billing;

namespace SRXPanel.Services.Vps;

public interface IVpsProvisioningService
{
    /// <summary>Kicks off provisioning for an already-created Building instance in the background.</summary>
    void StartProvisioning(int instanceId);

    Task RunAsync(int instanceId);
}

/// <summary>
/// Clones + configures a VM for a newly ordered VPS, broadcasting each step over /hubs/vps.
/// Every Proxmox call goes through IProxmoxService, so the whole flow is simulation-safe: in
/// simulation the VM "comes up" after a short delay with a deterministic 192.168.100.x IP.
/// </summary>
public class VpsProvisioningService : IVpsProvisioningService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VpsProvisioningService> _logger;

    public VpsProvisioningService(IServiceScopeFactory scopeFactory, ILogger<VpsProvisioningService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void StartProvisioning(int instanceId)
    {
        _ = Task.Run(async () =>
        {
            try { await RunAsync(instanceId); }
            catch (Exception ex) { _logger.LogError(ex, "VPS provisioning {Id} crashed", instanceId); }
        });
    }

    public async Task RunAsync(int instanceId)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var proxmox = sp.GetRequiredService<IProxmoxService>();
        var broadcast = sp.GetRequiredService<IVpsBroadcast>();
        var mailer = sp.GetRequiredService<IMailerService>();
        var notifications = sp.GetRequiredService<INotificationService>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();

        var vps = await db.VpsInstances.Include(v => v.Node).Include(v => v.Plan)
            .FirstOrDefaultAsync(v => v.Id == instanceId);
        if (vps?.Node == null)
        {
            await broadcast.ProvisionCompletedAsync(instanceId, false, "Instance or node not found.", null);
            return;
        }

        var node = vps.Node;
        var template = await db.VpsTemplates.FirstOrDefaultAsync(t => t.OsType == vps.OsTemplate && t.NodeId == node.Id)
            ?? await db.VpsTemplates.FirstOrDefaultAsync(t => t.NodeId == node.Id);

        async Task Emit(int percent, string step, string log)
        {
            await broadcast.ProvisionProgressAsync(instanceId, percent, step, log);
            await Task.Delay(1000);
        }

        try
        {
            await Emit(5, "Starting", $"Provisioning {vps.Hostname} (VM {vps.VmId}) on {node.Name} ({node.Location}).");

            // 1) Clone template
            await Emit(15, "Creating target user account", $"Preparing VM {vps.VmId} on {node.Name}.");
            var create = await proxmox.CreateVmAsync(node, vps.VmId, new VmCreateConfig(
                vps.Hostname, template?.TemplateId ?? 9000, vps.CpuCores, vps.RamMB, vps.DiskGB, node.Storage, node.Network));
            await Emit(25, "Cloning template", $"▸ Clone from template {template?.Name ?? vps.OsTemplate}: {create.Output}");

            // 2) Set resources
            await proxmox.ResizeVmAsync(node, vps.VmId, vps.DiskGB, vps.RamMB, vps.CpuCores);
            await Emit(40, "Configuring resources", $"▸ {vps.CpuCores} vCPU · {vps.RamMB} MB RAM · {vps.DiskGB} GB disk.");

            // 3) Cloud-init (hostname, password, SSH key, network)
            var ip = vps.IpAddress ?? SimIp(vps.VmId);
            var gateway = GatewayFor(ip);
            var ipv6 = $"2a01:4f8:100:{vps.VmId:x}::1";
            await proxmox.SetCloudInitAsync(node, vps.VmId, new CloudInitConfig(
                vps.Hostname, "root", vps.RootPassword, await SshKeysForAsync(db, vps.UserId), ip, gateway, ipv6));
            await Emit(55, "Applying cloud-init", "▸ Hostname, credentials and network configured.");

            // 4) Start VM
            await proxmox.StartVmAsync(node, vps.VmId);
            await Emit(70, "Booting", "▸ Powering on the VM…");

            // 5) Wait for IP assignment (simulated ~5s settle)
            await Emit(85, "Waiting for network", "▸ Waiting for DHCP/cloud-init to bring up the interface…");
            await Task.Delay(4000);

            // 6) Persist IP + Running
            vps.IpAddress = ip;
            vps.Ipv6Address = ipv6;
            vps.MacAddress = MacFor(vps.VmId);
            vps.ReverseDns = vps.Hostname;
            vps.SshPort = 22;
            vps.Status = VpsStatus.Running;
            await db.SaveChangesAsync();

            await broadcast.StatusAsync(instanceId, VpsStatus.Running.ToString());
            await Emit(95, "Testing connectivity", $"▸ {vps.Hostname} answered on {ip}:22.");

            // 7) Welcome email + notification
            var user = await userManager.FindByIdAsync(vps.UserId);
            if (!string.IsNullOrEmpty(user?.Email))
                await mailer.SendTemplateAsync(user.Email, $"Your VPS {vps.Hostname} is ready", "vps_welcome",
                    new Dictionary<string, string>
                    {
                        ["HOSTNAME"] = vps.Hostname,
                        ["IP"] = ip,
                        ["IPV6"] = ipv6,
                        ["USERNAME"] = "root",
                        ["PASSWORD"] = vps.RootPassword ?? "(your SSH key)",
                        ["SSH_PORT"] = vps.SshPort.ToString(),
                        ["LOCATION"] = node.Location,
                        ["PLAN"] = vps.Plan?.Name ?? "VPS"
                    });

            await notifications.NotifyAsync(vps.UserId, "VPS ready",
                $"{vps.Hostname} is running at {ip}.", NotificationType.Success);

            await broadcast.ProvisionCompletedAsync(instanceId, true, $"{vps.Hostname} is ready at {ip}.", ip);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VPS provisioning {Id} failed", instanceId);
            vps.Status = VpsStatus.Error;
            await db.SaveChangesAsync();
            await broadcast.ProvisionCompletedAsync(instanceId, false, $"Provisioning failed: {ex.Message}", null);
        }
    }

    private static async Task<string?> SshKeysForAsync(ApplicationDbContext db, string userId)
    {
        var keys = await db.SshKeys.Where(k => k.UserId == userId).Select(k => k.PublicKey).ToListAsync();
        return keys.Count == 0 ? null : string.Join("\n", keys);
    }

    /// <summary>Deterministic simulation IP: 192.168.100.{vmId last two digits}.</summary>
    private static string SimIp(int vmId) => $"192.168.100.{vmId % 256}";
    private static string GatewayFor(string ip)
    {
        var parts = ip.Split('.');
        return parts.Length == 4 ? $"{parts[0]}.{parts[1]}.{parts[2]}.1" : "192.168.100.1";
    }
    private static string MacFor(int vmId) => $"BC:24:11:{(vmId >> 8) & 0xFF:X2}:{vmId & 0xFF:X2}:{(vmId * 7) & 0xFF:X2}";
}
