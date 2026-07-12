using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services.Developer;

namespace SRXPanel.Pages.Client.Developer;

public class TerminalModel : PageModel
{
    private readonly ITerminalService _terminals;
    private readonly ISshKeyService _ssh;
    private readonly UserManager<ApplicationUser> _userManager;

    public TerminalModel(ITerminalService terminals, ISshKeyService ssh, UserManager<ApplicationUser> userManager)
    {
        _terminals = terminals;
        _ssh = ssh;
        _userManager = userManager;
    }

    public string Username { get; private set; } = "";
    public int ActiveSessions { get; private set; }
    public int MaxSessions => ITerminalService.MaxConcurrentSessions;
    public bool SshEnabled { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        Username = user.UserName ?? "";
        ActiveSessions = await _terminals.CountActiveAsync(user.Id);
        SshEnabled = (await _ssh.GetAccessAsync(user.Id)).IsEnabled;

        return Page();
    }

    /// <summary>Mints the short-lived token the WebSocket endpoint accepts. Called by the page on connect.</summary>
    public async Task<IActionResult> OnPostTicketAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (await _terminals.CountActiveAsync(user.Id) >= ITerminalService.MaxConcurrentSessions)
            return BadRequest(new { error = $"You already have {ITerminalService.MaxConcurrentSessions} terminal sessions open." });

        var ticket = await _terminals.CreateTicketAsync(user.Id);
        return new JsonResult(new { token = ticket.Token, expiresAt = ticket.ExpiresAt });
    }
}
