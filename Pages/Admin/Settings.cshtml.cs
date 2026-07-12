using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Pages.Admin;

public class SettingsModel : PageModel
{
    private readonly IPlatformSettingsService _platform;
    private readonly ICommandRunner _log;
    private readonly IAuditLogService _audit;
    private readonly ISettingsWriter _settingsWriter;
    private readonly Microsoft.Extensions.Options.IOptionsMonitor<BackupSettings> _backupSettings;

    public SettingsModel(IPlatformSettingsService platform, ICommandRunner log, IAuditLogService audit,
        ISettingsWriter settingsWriter, Microsoft.Extensions.Options.IOptionsMonitor<BackupSettings> backupSettings)
    {
        _platform = platform;
        _log = log;
        _audit = audit;
        _settingsWriter = settingsWriter;
        _backupSettings = backupSettings;
    }

    [BindProperty] public PlatformSettings Settings { get; set; } = new();
    [BindProperty] public BackupSettings Backup { get; set; } = new();
    [TempData] public string? SmtpTestResult { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        Settings = await _platform.GetAsync();
        Backup = _backupSettings.CurrentValue;
        return Page();
    }

    public async Task<IActionResult> OnPostBackupAsync()
    {
        await _settingsWriter.SaveBackupAsync(Backup);
        await _audit.LogAsync("Update", "BackupSettings", "1", $"off-site backup ({Backup.Provider}) configured");
        TempData["Success"] = "Off-site backup settings saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await _platform.SaveAsync(Settings);
        await _audit.LogAsync("Update", "PlatformSettings", "1", "platform settings updated");
        TempData["Success"] = "Platform settings saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestSmtpAsync()
    {
        // Simulation-safe SMTP connectivity test.
        await _log.LogExternalAsync("smtp.test(connect + auth)", "250 OK — connection succeeded (simulated)", true, "smtp");
        SmtpTestResult = "SMTP test succeeded (simulated). Check the Command Log for details.";
        return RedirectToPage();
    }
}
