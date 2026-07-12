using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Developer;

namespace SRXPanel.Pages.Client.Cron;

public class IndexModel : PageModel
{
    private readonly ICronService _cron;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _auditLog;

    public IndexModel(ICronService cron, UserManager<ApplicationUser> userManager, IAuditLogService auditLog)
    {
        _cron = cron;
        _userManager = userManager;
        _auditLog = auditLog;
    }

    public List<CronJob> Jobs { get; private set; } = new();
    public Dictionary<int, string> Descriptions { get; private set; } = new();

    public int Used { get; private set; }
    public int? Limit { get; private set; }
    public bool AtLimit => Limit.HasValue && Used >= Limit.Value;

    [BindProperty] public CronInput Input { get; set; } = new();
    [BindProperty(SupportsGet = true)] public int? EditId { get; set; }

    public CronJob? EditJob { get; private set; }

    private async Task<string?> UserIdAsync() => (await _userManager.GetUserAsync(User))?.Id;

    public async Task<IActionResult> OnGetAsync()
    {
        var userId = await UserIdAsync();
        if (userId == null) return Challenge();

        await LoadAsync(userId);

        if (EditId is int id)
        {
            EditJob = Jobs.FirstOrDefault(j => j.Id == id);
            if (EditJob != null)
            {
                Input = new CronInput
                {
                    Command = EditJob.Command,
                    Schedule = EditJob.Schedule,
                    Description = EditJob.Description,
                    Email = EditJob.Email,
                    EmailOnSuccess = EditJob.EmailOnSuccess,
                    EmailOnFailure = EditJob.EmailOnFailure
                };
            }
        }

        return Page();
    }

    private async Task LoadAsync(string userId)
    {
        Jobs = await _cron.GetJobsAsync(userId);
        (Used, Limit) = await _cron.GetQuotaAsync(userId);

        foreach (var job in Jobs)
        {
            var validation = _cron.ValidateCronExpression(job.Schedule);
            Descriptions[job.Id] = validation.IsValid ? validation.Description : "Invalid schedule";
        }
    }

    /// <summary>Live validation for the schedule builder — returns the readable preview and next run.</summary>
    public IActionResult OnGetValidate(string expression)
    {
        var validation = _cron.ValidateCronExpression(expression ?? "");
        return new JsonResult(new
        {
            valid = validation.IsValid,
            description = validation.Description,
            error = validation.Error,
            nextRun = validation.NextRun?.ToString("yyyy-MM-dd HH:mm 'UTC'")
        });
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var userId = await UserIdAsync();
        if (userId == null) return Challenge();

        try
        {
            var job = await _cron.CreateJobAsync(userId, Input.Command, Input.Schedule, Input.Email,
                Input.Description, Input.EmailOnSuccess, Input.EmailOnFailure);

            await _auditLog.LogAsync("Create", "CronJob", job.Id.ToString(), job.Command);
            TempData["Success"] = $"Cron job created. Next run: {job.NextRunAt:yyyy-MM-dd HH:mm} UTC.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(int id)
    {
        var userId = await UserIdAsync();
        if (userId == null) return Challenge();

        try
        {
            await _cron.UpdateJobAsync(userId, id, Input.Command, Input.Schedule, Input.Description,
                Input.Email, Input.EmailOnSuccess, Input.EmailOnFailure);

            await _auditLog.LogAsync("Update", "CronJob", id.ToString(), Input.Command);
            TempData["Success"] = "Cron job updated.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var userId = await UserIdAsync();
        if (userId == null) return Challenge();

        await _cron.DeleteJobAsync(userId, id);
        await _auditLog.LogAsync("Delete", "CronJob", id.ToString(), "");
        TempData["Success"] = "Cron job deleted.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id, bool enable)
    {
        var userId = await UserIdAsync();
        if (userId == null) return Challenge();

        if (enable) await _cron.EnableJobAsync(userId, id);
        else await _cron.DisableJobAsync(userId, id);

        TempData["Success"] = enable ? "Cron job enabled." : "Cron job disabled.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRunNowAsync(int id)
    {
        var userId = await UserIdAsync();
        if (userId == null) return Challenge();

        try
        {
            await _cron.RunNowAsync(userId, id);
            await _auditLog.LogAsync("Run", "CronJob", id.ToString(), "manual trigger");
            TempData["Success"] = "Job started. Refresh the execution log in a moment to see the result.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage("/Client/Cron/Logs", new { id });
    }
}

public class CronInput
{
    public string Command { get; set; } = string.Empty;
    public string Schedule { get; set; } = "0 3 * * *";
    public string? Description { get; set; }
    public string? Email { get; set; }
    public bool EmailOnSuccess { get; set; }
    public bool EmailOnFailure { get; set; } = true;
}
