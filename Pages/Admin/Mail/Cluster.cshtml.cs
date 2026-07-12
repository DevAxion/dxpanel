using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Nodes;

namespace SRXPanel.Pages.Admin.Mail;

public class ClusterModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly INodeSshService _ssh;

    public ClusterModel(ApplicationDbContext db, INodeSshService ssh)
    {
        _db = db;
        _ssh = ssh;
    }

    public List<ServerNode> MailServers { get; private set; } = new();
    public int MailboxCount { get; private set; }

    public async Task OnGetAsync()
    {
        MailServers = await _db.ServerNodes
            .Where(n => n.Type == NodeType.Mail && n.IsActive)
            .OrderBy(n => n.Name).ToListAsync();
        MailboxCount = await _db.EmailAccounts.CountAsync();
    }

    public async Task<IActionResult> OnPostSyncAsync()
    {
        var servers = await _db.ServerNodes.Where(n => n.Type == NodeType.Mail && n.IsActive).ToListAsync();
        var primary = servers.FirstOrDefault();
        if (primary == null) { TempData["Error"] = "No mail node found."; return RedirectToPage(); }

        foreach (var relay in servers.Skip(1))
            await _ssh.ExecuteCommandAsync(relay, $"rsync -az {primary.IpAddress}:/var/mail/vhosts/ /var/mail/vhosts/  # maildir replication");

        TempData["Success"] = $"Maildir replication triggered to {servers.Count - 1} relay server(s).";
        return RedirectToPage();
    }
}
