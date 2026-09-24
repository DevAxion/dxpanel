using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using DXPanel.Models;
using DXPanel.Services;

namespace DXPanel.Pages.Docs;

[AllowAnonymous]
public class IndexModel : PageModel
{
    private readonly PublicSiteOptions _site;

    public IndexModel(IOptions<PublicSiteOptions> site)
    {
        _site = site.Value;
    }

    public IActionResult OnGet()
    {
        // Developer docs are hidden by default; only the SuperAdmin can see them
        // unless DeveloperDocsPublic is explicitly enabled in configuration.
        if (!_site.DeveloperDocsPublic && !User.IsInRole(Roles.SuperAdmin))
        {
            return Redirect("/");
        }
        return Page();
    }
}
