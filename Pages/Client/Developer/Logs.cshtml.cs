using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services.Developer;

namespace SRXPanel.Pages.Client.Developer;

public class LogsModel : PageModel
{
    private readonly ILogViewerService _logs;
    private readonly UserManager<ApplicationUser> _userManager;

    public LogsModel(ILogViewerService logs, UserManager<ApplicationUser> userManager)
    {
        _logs = logs;
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)] public string? Source { get; set; }
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public LogLevelFilter Level { get; set; } = LogLevelFilter.All;
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }

    public List<LogSource> Sources { get; private set; } = new();
    public LogSource? Selected { get; private set; }
    public List<LogLine> Lines { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        Sources = await _logs.GetSourcesAsync(user.Id);
        Source ??= Sources.FirstOrDefault()?.Id;
        if (Source == null) return Page();

        Selected = await _logs.GetSourceAsync(user.Id, Source);
        if (Selected == null) return NotFound();

        Lines = await _logs.ReadAsync(user.Id, Source, new LogQuery(Search, Level, From, To, 1000));
        return Page();
    }

    /// <summary>Polling fallback for the live tail.</summary>
    public async Task<IActionResult> OnGetTailAsync(string source)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var lines = await _logs.TailAsync(user.Id, source, 100);
        return new JsonResult(lines.Select(l => new { text = l.Text, level = l.Level.ToString() }));
    }

    public async Task<IActionResult> OnGetDownloadAsync(string source)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (await _logs.GetSourceAsync(user.Id, source) == null) return NotFound();

        var (stream, fileName) = await _logs.DownloadAsync(user.Id, source);
        return File(stream, "text/plain", fileName);
    }

    public async Task<IActionResult> OnPostClearAsync(string source)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (await _logs.GetSourceAsync(user.Id, source) == null) return NotFound();

        await _logs.ClearAsync(user.Id, source);
        TempData["Success"] = "Log cleared.";
        return RedirectToPage(new { source });
    }
}
