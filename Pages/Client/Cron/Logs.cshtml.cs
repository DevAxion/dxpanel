using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services.Developer;

namespace SRXPanel.Pages.Client.Cron;

public class LogsModel : PageModel
{
    private readonly ICronService _cron;
    private readonly UserManager<ApplicationUser> _userManager;

    public LogsModel(ICronService cron, UserManager<ApplicationUser> userManager)
    {
        _cron = cron;
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)] public int Id { get; set; }

    public CronJob Job { get; private set; } = null!;
    public List<CronJobLog> Logs { get; private set; } = new();
    public string ScheduleDescription { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var job = await _cron.GetJobAsync(user.Id, Id);
        if (job == null) return NotFound();

        Job = job;
        Logs = await _cron.GetJobLogAsync(user.Id, Id, 25);

        var validation = _cron.ValidateCronExpression(job.Schedule);
        ScheduleDescription = validation.IsValid ? validation.Description : "Invalid schedule";

        return Page();
    }

    public async Task<IActionResult> OnPostRunNowAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        try
        {
            await _cron.RunNowAsync(user.Id, Id);
            TempData["Success"] = "Job started.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage(new { id = Id });
    }
}
