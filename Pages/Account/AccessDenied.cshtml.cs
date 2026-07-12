using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SRXPanel.Pages.Account;

[AllowAnonymous]
public class AccessDeniedModel : PageModel
{
    public void OnGet()
    {
    }
}
