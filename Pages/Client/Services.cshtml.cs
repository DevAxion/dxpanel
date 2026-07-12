using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services.Store;

namespace SRXPanel.Pages.Client;

public class ServicesModel : PageModel
{
    private readonly IStoreService _store;
    private readonly UserManager<ApplicationUser> _userManager;

    public ServicesModel(IStoreService store, UserManager<ApplicationUser> userManager)
    {
        _store = store;
        _userManager = userManager;
    }

    public List<ServiceView> Services { get; private set; } = new();
    public List<ClientAddon> Addons { get; private set; } = new();
    public List<DomainRegistration> Domains { get; private set; } = new();

    // Diagnostic counts, shown only when ?debug=1 is present in the URL.
    [BindProperty(SupportsGet = true)] public bool Debug { get; set; }
    public int SharedCount { get; private set; }
    public int VpsCount { get; private set; }
    public int ResellerCount { get; private set; }

    public async Task OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return;

        Services = await _store.GetActiveServicesAsync(user.Id);
        Addons = await _store.GetActiveAddonsAsync(user.Id);
        Domains = await _store.GetDomainRegistrationsAsync(user.Id);

        SharedCount = Services.Count(s => s.Kind == ServiceKind.Shared);
        VpsCount = Services.Count(s => s.Kind == ServiceKind.Vps);
        ResellerCount = Services.Count(s => s.Kind == ServiceKind.Reseller);
    }

    public async Task<IActionResult> OnPostCancelAsync(int subscriptionId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var (ok, message) = await _store.CancelServiceAsync(user, subscriptionId);
        TempData[ok ? "Success" : "Error"] = message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCancelServiceAsync(int clientServiceId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var (ok, message) = await _store.CancelClientServiceAsync(user, clientServiceId);
        TempData[ok ? "Success" : "Error"] = message;
        return RedirectToPage();
    }
}
