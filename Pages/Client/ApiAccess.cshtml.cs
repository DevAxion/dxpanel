using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Portal;

namespace SRXPanel.Pages.Client;

public class ApiAccessModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IApiKeyService _apiKeys;

    public ApiAccessModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IApiKeyService apiKeys)
    {
        _db = db;
        _userManager = userManager;
        _apiKeys = apiKeys;
    }

    public List<ApiKey> Keys { get; set; } = new();
    public List<WebhookEndpoint> Webhooks { get; set; } = new();
    public int CallsToday { get; set; }
    public int Calls7Days { get; set; }
    public List<(string Day, int Count)> Daily { get; set; } = new();
    [TempData] public string? NewKey { get; set; }

    private async Task<ApplicationUser?> LoadAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return null;
        Keys = await _apiKeys.ListAsync(user.Id);
        Webhooks = await _db.WebhookEndpoints.Where(w => w.UserId == user.Id).OrderByDescending(w => w.Id).ToListAsync();

        var prefixes = Keys.Select(k => k.Prefix).ToList();
        var since = DateTime.UtcNow.Date.AddDays(-6);
        var logs = await _db.ApiRequestLogs
            .Where(l => l.KeyPrefix != null && prefixes.Contains(l.KeyPrefix) && l.CreatedAt >= since)
            .ToListAsync();
        CallsToday = logs.Count(l => l.CreatedAt.Date == DateTime.UtcNow.Date);
        Calls7Days = logs.Count;
        Daily = Enumerable.Range(0, 7)
            .Select(i => DateTime.UtcNow.Date.AddDays(-6 + i))
            .Select(d => (d.ToString("MM-dd"), logs.Count(l => l.CreatedAt.Date == d)))
            .ToList();
        return user;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (await LoadAsync() == null) return Challenge();
        return Page();
    }

    public async Task<IActionResult> OnPostGenerateAsync(string name)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var (_, plaintext) = await _apiKeys.GenerateAsync(user.Id, string.IsNullOrWhiteSpace(name) ? "API Key" : name.Trim());
        NewKey = plaintext;
        TempData["Success"] = "API key generated. Copy it now — it is shown only once.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRevokeAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        await _apiKeys.RevokeAsync(user.Id, id);
        TempData["Success"] = "API key revoked.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddWebhookAsync(string url, bool onDomain, bool onEmail, bool onSsl, bool onInvoice)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            TempData["Error"] = "Enter a valid absolute URL.";
            return RedirectToPage();
        }
        _db.WebhookEndpoints.Add(new WebhookEndpoint
        {
            UserId = user.Id,
            Url = url.Trim(),
            Secret = Guid.NewGuid().ToString("N")[..16],
            OnDomainChange = onDomain,
            OnEmailChange = onEmail,
            OnSslExpiring = onSsl,
            OnInvoicePaid = onInvoice,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Webhook endpoint added.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteWebhookAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var wh = await _db.WebhookEndpoints.FirstOrDefaultAsync(w => w.Id == id && w.UserId == user.Id);
        if (wh != null) { _db.WebhookEndpoints.Remove(wh); await _db.SaveChangesAsync(); }
        TempData["Success"] = "Webhook removed.";
        return RedirectToPage();
    }
}
