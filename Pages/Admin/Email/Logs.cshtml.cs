using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Email;

namespace SRXPanel.Pages.Admin.Email;

public class LogsModel : PageModel
{
    private readonly IEmailLogService _logs;
    private readonly ApplicationDbContext _db;

    public LogsModel(IEmailLogService logs, ApplicationDbContext db)
    {
        _logs = logs;
        _db = db;
    }

    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true)] public EmailLogStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public int? DomainId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int Page { get; set; } = 1;

    public PagedLogs Logs { get; private set; } = new(new(), 1, 1, 0);
    public DeliveryReport Report { get; private set; } = new(0, 0, 0, 0, 0, 0);
    public List<Domain> Domains { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Domains = await _db.Domains.OrderBy(d => d.DomainName).ToListAsync();
        var from = From ?? DateTime.UtcNow.AddDays(-7);
        var to = (To ?? DateTime.UtcNow).Date.AddDays(1).AddSeconds(-1);

        if (!string.IsNullOrWhiteSpace(Q))
            Logs = new PagedLogs(await _logs.SearchLogsAsync(null, Q), 1, 1, 0);
        else
            Logs = await _logs.GetLogsAsync(null, DomainId, from, to, Status, Page);

        Report = await _logs.GetDeliveryReportAsync(null, DomainId, from, to);
    }

    public async Task<IActionResult> OnGetExportAsync()
    {
        var from = From ?? DateTime.UtcNow.AddDays(-30);
        var to = (To ?? DateTime.UtcNow).Date.AddDays(1).AddSeconds(-1);
        var csv = await _logs.ExportLogsAsync(null, DomainId, from, to);
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"platform-email-logs-{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}
