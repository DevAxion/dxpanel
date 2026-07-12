using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services;

namespace SRXPanel.Pages.Admin;

public class FrontendModel : PageModel
{
    private readonly IFrontendService _frontend;
    private readonly IAuditLogService _audit;

    public FrontendModel(IFrontendService frontend, IAuditLogService audit)
    {
        _frontend = frontend;
        _audit = audit;
    }

    [BindProperty] public FrontendSettings Settings { get; set; } = new();

    public async Task OnGetAsync()
    {
        Settings = await _frontend.GetSettingsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        Settings.Id = 1;
        await _frontend.SaveSettingsAsync(Settings);
        await _audit.LogAsync("Update", "FrontendSettings", "1", "frontend settings updated");
        TempData["Success"] = "Frontend settings saved.";
        return RedirectToPage();
    }
}
