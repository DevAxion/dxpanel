using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Pages.Admin;

public class AlertsModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public AlertsModel(ApplicationDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public string Filter { get; set; } = "unacked";

    public List<NodeAlert> Alerts { get; private set; } = new();
    public int Critical { get; private set; }
    public int Warning { get; private set; }
    public int Unacknowledged { get; private set; }

    public async Task OnGetAsync()
    {
        var query = _db.NodeAlerts.Include(a => a.Node).AsQueryable();
        query = Filter switch
        {
            "unacked" => query.Where(a => !a.IsAcknowledged),
            "critical" => query.Where(a => a.Severity == AlertSeverity.Critical),
            _ => query
        };

        Alerts = await query.OrderByDescending(a => a.CreatedAt).Take(100).ToListAsync();

        Unacknowledged = await _db.NodeAlerts.CountAsync(a => !a.IsAcknowledged);
        Critical = await _db.NodeAlerts.CountAsync(a => !a.IsAcknowledged && a.Severity == AlertSeverity.Critical);
        Warning = await _db.NodeAlerts.CountAsync(a => !a.IsAcknowledged && a.Severity == AlertSeverity.Warning);
    }

    public async Task<IActionResult> OnPostAcknowledgeAsync(int id)
    {
        var alert = await _db.NodeAlerts.FirstOrDefaultAsync(a => a.Id == id);
        if (alert != null)
        {
            alert.IsAcknowledged = true;
            alert.AcknowledgedAt = DateTime.UtcNow;
            alert.AcknowledgedBy = User.Identity?.Name;
            await _db.SaveChangesAsync();
        }
        return RedirectToPage(new { filter = Filter });
    }

    public async Task<IActionResult> OnPostAcknowledgeAllAsync()
    {
        var pending = await _db.NodeAlerts.Where(a => !a.IsAcknowledged).ToListAsync();
        foreach (var alert in pending)
        {
            alert.IsAcknowledged = true;
            alert.AcknowledgedAt = DateTime.UtcNow;
            alert.AcknowledgedBy = User.Identity?.Name;
        }
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Acknowledged {pending.Count} alert(s).";
        return RedirectToPage(new { filter = Filter });
    }
}
