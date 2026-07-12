using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using SRXPanel.Services;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Pages.Settings;

public class IndexModel : PageModel
{
    private readonly IOptionsMonitor<PanelSettings> _settings;
    private readonly ISettingsWriter _writer;
    private readonly IAuditLogService _audit;
    private readonly ICommandRunner _runner;

    public IndexModel(IOptionsMonitor<PanelSettings> settings, ISettingsWriter writer, IAuditLogService audit, ICommandRunner runner)
    {
        _settings = settings;
        _writer = writer;
        _audit = audit;
        _runner = runner;
    }

    [BindProperty]
    public PanelSettings Input { get; set; } = new();

    [BindProperty]
    public bool SimulationMode { get; set; }

    public bool EffectiveSimulationMode => _runner.SimulationMode;
    public string[] PhpVersions => PanelSettings.PhpVersions;

    public void OnGet()
    {
        Input = Clone(_settings.CurrentValue);
        SimulationMode = _writer.CurrentSimulationMode;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        await _writer.SaveAsync(Input, SimulationMode);
        await _audit.LogAsync("Update", "Settings", null, $"Panel settings saved (SimulationMode={SimulationMode})");

        TempData["Success"] = "Settings saved to appsettings.json. Changes apply immediately.";
        return RedirectToPage();
    }

    private static PanelSettings Clone(PanelSettings s) => new()
    {
        Hostname = s.Hostname,
        DefaultPhpVersion = s.DefaultPhpVersion,
        MaxUploadSizeMB = s.MaxUploadSizeMB,
        LetsEncryptEmail = s.LetsEncryptEmail,
        SshKeyPath = s.SshKeyPath,
        Smtp = new SmtpSettings
        {
            Host = s.Smtp.Host, Port = s.Smtp.Port, User = s.Smtp.User,
            Password = s.Smtp.Password, From = s.Smtp.From
        },
        MySql = new MySqlSettings
        {
            Host = s.MySql.Host, Port = s.MySql.Port,
            RootUser = s.MySql.RootUser, RootPassword = s.MySql.RootPassword
        },
        Nginx = s.Nginx,
        Bind = s.Bind,
        Mail = s.Mail
    };
}
