using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.AppHosting;

namespace SRXPanel.Pages.Admin.Apps;

public class RuntimesModel : PageModel
{
    private readonly IRuntimeService _runtimes;
    private readonly IAuditLogService _auditLog;

    public RuntimesModel(IRuntimeService runtimes, IAuditLogService auditLog)
    {
        _runtimes = runtimes;
        _auditLog = auditLog;
    }

    public Dictionary<AppRuntimeType, List<AppRuntime>> Installed { get; private set; } = new();
    public Dictionary<AppRuntimeType, IReadOnlyList<string>> Installable { get; private set; } = new();

    public async Task OnGetAsync()
    {
        foreach (var type in Enum.GetValues<AppRuntimeType>())
        {
            Installed[type] = await _runtimes.GetInstalledVersionsAsync(type);
            Installable[type] = _runtimes.GetInstallableVersions(type);
        }
    }

    public async Task<IActionResult> OnPostInstallAsync(AppRuntimeType type, string version)
    {
        await _runtimes.InstallRuntimeAsync(type, version);
        await _auditLog.LogAsync("InstallRuntime", "AppRuntime", "", $"{type} {version}");
        TempData["Success"] = $"{type} {version} installed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(int runtimeId)
    {
        try
        {
            await _runtimes.RemoveRuntimeAsync(runtimeId);
            TempData["Success"] = "Runtime removed.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDefaultAsync(int runtimeId)
    {
        await _runtimes.SetDefaultAsync(runtimeId);
        TempData["Success"] = "Default version updated.";
        return RedirectToPage();
    }
}
