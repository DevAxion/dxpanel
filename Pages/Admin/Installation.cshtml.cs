using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SRXPanel.Pages.Admin;

// Authorized via the "/Admin" folder convention (SuperAdminOnly).
public class InstallationModel : PageModel
{
    public string Version => AppInfo.Version;

    public void OnGet() { }
}
