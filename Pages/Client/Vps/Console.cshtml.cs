using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DXPanel.Pages.Client.Vps;

public class ConsoleModel : PageModel
{
    [BindProperty(SupportsGet = true)] public int VmId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Token { get; set; }

    public void OnGet() { }
}
