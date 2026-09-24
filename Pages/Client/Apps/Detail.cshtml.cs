using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using DXPanel.Models;
using DXPanel.Services.Apps;

namespace DXPanel.Pages.Client.Apps;

public class DetailModel : PageModel
{
    private readonly IAppInstallerService _installer;

    public DetailModel(IAppInstallerService installer)
    {
        _installer = installer;
    }

    public AppDefinition App { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        var app = await _installer.GetAppAsync(slug);
        if (app == null) return NotFound();
        App = app;
        return Page();
    }
}
