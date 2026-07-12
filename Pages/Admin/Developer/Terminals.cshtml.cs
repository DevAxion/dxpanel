using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Developer;

namespace SRXPanel.Pages.Admin.Developer;

public class TerminalsModel : PageModel
{
    private readonly ITerminalService _terminals;
    private readonly IAuditLogService _auditLog;

    public TerminalsModel(ITerminalService terminals, IAuditLogService auditLog)
    {
        _terminals = terminals;
        _auditLog = auditLog;
    }

    public List<TerminalSession> Active { get; private set; } = new();
    public List<TerminalSession> History { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Active = await _terminals.GetActiveSessionsAsync();
        History = (await _terminals.GetSessionHistoryAsync(50)).Where(s => !s.IsActive).ToList();
    }

    public async Task<IActionResult> OnPostTerminateAsync(int id)
    {
        await _terminals.TerminateAsync(id);
        await _auditLog.LogAsync("Terminate", "TerminalSession", id.ToString(), "terminated by admin");

        TempData["Success"] = "Termination requested. The session closes on its next keystroke or within a few minutes.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostReapAsync()
    {
        var count = await _terminals.ReapIdleSessionsAsync(TerminalService.IdleTimeout);
        TempData["Success"] = count == 0 ? "No idle sessions to close." : $"Closed {count} idle session(s).";
        return RedirectToPage();
    }
}
