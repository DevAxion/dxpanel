using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DXPanel.Pages;

[AllowAnonymous]
public class FeaturesModel : PageModel
{
    public void OnGet() { }
}
