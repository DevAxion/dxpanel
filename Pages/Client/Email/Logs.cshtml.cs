using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Email;

namespace SRXPanel.Pages.Client.Email;

public class LogsModel : PageModel
{
    private readonly IEmailLogService _logs;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;

    public LogsModel(IEmailLogService logs, UserManager<ApplicationUser> userManager, ApplicationDbContext db)
    {
        _logs = logs;
        _userManager = userManager;
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

    private string Uid => _userManager.GetUserId(User)!;

    public async Task OnGetAsync()
    {
        Domains = await _db.Domains.Where(d => d.UserId == Uid).ToListAsync();
        var from = From ?? DateTime.UtcNow.AddDays(-7);
        var to = (To ?? DateTime.UtcNow).Date.AddDays(1).AddSeconds(-1);

        if (!string.IsNullOrWhiteSpace(Q))
            Logs = new PagedLogs(await _logs.SearchLogsAsync(Uid, Q), 1, 1, 0);
        else
            Logs = await _logs.GetLogsAsync(Uid, DomainId, from, to, Status, Page);

        Report = await _logs.GetDeliveryReportAsync(Uid, DomainId, from, to);
    }

    public async Task<IActionResult> OnGetDetailsAsync(string messageId)
    {
        var detail = await _logs.GetLogDetailsAsync(messageId, Uid);
        if (detail == null) return NotFound();
        return new JsonResult(new
        {
            headers = detail.Headers,
            preview = detail.BodyPreview,
            status = detail.Log.Status.ToString(),
            spamScore = detail.Log.SpamScore
        });
    }

    public async Task<IActionResult> OnGetExportAsync()
    {
        var from = From ?? DateTime.UtcNow.AddDays(-30);
        var to = (To ?? DateTime.UtcNow).Date.AddDays(1).AddSeconds(-1);
        var csv = await _logs.ExportLogsAsync(Uid, DomainId, from, to);
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"email-logs-{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}
