using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Email;

namespace SRXPanel.Pages.Client.Email;

public class QueueModel : PageModel
{
    private readonly IEmailQueueService _queue;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;

    public QueueModel(IEmailQueueService queue, UserManager<ApplicationUser> userManager, ApplicationDbContext db)
    {
        _queue = queue;
        _userManager = userManager;
        _db = db;
    }

    [BindProperty(SupportsGet = true)] public EmailQueueStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public int Page { get; set; } = 1;

    public PagedQueue Queue { get; private set; } = new(new(), 1, 1, 0);
    public QueueCounts Counts { get; private set; } = new(0, 0, 0, 0, 0, 0);
    public double DeliveryRate { get; private set; }
    public bool AnyPaused { get; private set; }
    public List<Domain> Domains { get; private set; } = new();

    private string Uid => _userManager.GetUserId(User)!;

    public async Task OnGetAsync()
    {
        Queue = await _queue.GetQueueAsync(Uid, Status, Page);
        Counts = await _queue.GetQueueSizeAsync(Uid);
        DeliveryRate = await _queue.GetDeliveryRateAsync(Uid);
        Domains = await _db.Domains.Where(d => d.UserId == Uid).ToListAsync();
        var ids = Domains.Select(d => d.Id).ToList();
        AnyPaused = await _db.MailServerConfigs.AnyAsync(c => ids.Contains(c.DomainId) && c.QueuePaused);
    }

    public async Task<IActionResult> OnPostRetryAsync(int id)
    {
        await _queue.RetryFailedAsync(id, Uid);
        TempData["Success"] = "Message requeued.";
        return RedirectToPage(new { Status, Page });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await _queue.DeleteQueuedAsync(id, Uid);
        TempData["Success"] = "Message removed from the queue.";
        return RedirectToPage(new { Status, Page });
    }

    public async Task<IActionResult> OnPostRetryAllAsync()
    {
        var n = await _queue.RetryAllFailedAsync(Uid);
        TempData["Success"] = $"{n} message(s) requeued.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostFlushAsync()
    {
        var n = await _queue.FlushQueueAsync(Uid);
        TempData["Success"] = $"{n} message(s) flushed from the queue.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTogglePauseAsync(bool pause)
    {
        var domains = await _db.Domains.Where(d => d.UserId == Uid).Select(d => d.Id).ToListAsync();
        foreach (var id in domains)
        {
            if (pause) await _queue.PauseQueueAsync(id); else await _queue.ResumeQueueAsync(id);
        }
        TempData["Success"] = pause ? "Queue paused." : "Queue resumed.";
        return RedirectToPage();
    }
}
