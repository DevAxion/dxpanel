using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services.Email;

namespace SRXPanel.Pages.Admin.Email;

public class QueueModel : PageModel
{
    private readonly IEmailQueueService _queue;

    public QueueModel(IEmailQueueService queue) => _queue = queue;

    [BindProperty(SupportsGet = true)] public EmailQueueStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public int Page { get; set; } = 1;

    public PagedQueue Queue { get; private set; } = new(new(), 1, 1, 0);
    public QueueCounts Counts { get; private set; } = new(0, 0, 0, 0, 0, 0);

    public async Task OnGetAsync()
    {
        Queue = await _queue.GetQueueAsync(null, Status, Page);
        Counts = await _queue.GetQueueSizeAsync();
    }

    public async Task<IActionResult> OnPostRetryAllAsync()
    {
        var n = await _queue.RetryAllFailedAsync();
        TempData["Success"] = $"{n} message(s) requeued platform-wide.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostFlushAsync()
    {
        var n = await _queue.FlushQueueAsync();
        TempData["Success"] = $"{n} message(s) flushed platform-wide.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRetryAsync(int id)
    {
        await _queue.RetryFailedAsync(id);
        return RedirectToPage(new { Status, Page });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await _queue.DeleteQueuedAsync(id);
        return RedirectToPage(new { Status, Page });
    }
}
