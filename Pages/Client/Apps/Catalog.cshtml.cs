using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using DXPanel.Models;
using DXPanel.Services.Apps;

namespace DXPanel.Pages.Client.Apps;

public class CatalogModel : PageModel
{
    private readonly IAppInstallerService _installer;

    public CatalogModel(IAppInstallerService installer)
    {
        _installer = installer;
    }

    [BindProperty(SupportsGet = true)] public AppCategory? Category { get; set; }
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }

    public List<AppDefinition> Apps { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Apps = await _installer.GetAvailableAppsAsync(Category, Search);
    }
}
