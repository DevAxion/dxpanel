using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services.Developer;
using SRXPanel.Services.Portal;

namespace SRXPanel.Pages.Client.Developer;

public class SettingsModel : PageModel
{
    private readonly IDeveloperSettingsService _settings;
    private readonly IApiKeyService _apiKeys;
    private readonly UserManager<ApplicationUser> _userManager;

    public SettingsModel(IDeveloperSettingsService settings, IApiKeyService apiKeys,
        UserManager<ApplicationUser> userManager)
    {
        _settings = settings;
        _apiKeys = apiKeys;
        _userManager = userManager;
    }

    public DeveloperSettings Settings { get; private set; } = new();
    public List<ApiKey> ApiKeys { get; private set; } = new();
    public List<WebhookEndpoint> Webhooks { get; private set; } = new();
    public List<WebhookDelivery> Deliveries { get; private set; } = new();
    public IReadOnlyList<WebhookEvent> Events => _settings.AvailableEvents;

    /// <summary>Plaintext API key, shown exactly once after generation.</summary>
    public string? NewApiKey { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        await LoadAsync(user.Id);
        NewApiKey = TempData["NewApiKey"] as string;
        return Page();
    }

    private async Task LoadAsync(string userId)
    {
        Settings = await _settings.GetAsync(userId);
        ApiKeys = await _apiKeys.ListAsync(userId);
        Webhooks = await _settings.GetWebhooksAsync(userId);
        Deliveries = await _settings.GetDeliveriesAsync(userId, null, 25);
    }

    public async Task<IActionResult> OnPostSaveAsync(bool debugMode, string? errorReportingEmail)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        try
        {
            await _settings.SaveAsync(user.Id, debugMode, errorReportingEmail);
            TempData["Success"] = "Developer settings saved.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostGenerateKeyAsync(string name)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Give the API key a name.";
            return RedirectToPage();
        }

        var (_, plaintext) = await _apiKeys.GenerateAsync(user.Id, name.Trim());
        TempData["NewApiKey"] = plaintext;
        TempData["Success"] = "API key generated. Copy it now — it is never shown again.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRevokeKeyAsync(int keyId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        await _apiKeys.RevokeAsync(user.Id, keyId);
        TempData["Success"] = "API key revoked.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddWebhookAsync(string url, bool onDomain, bool onEmail, bool onSsl, bool onInvoice)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        try
        {
            var endpoint = await _settings.AddWebhookAsync(user.Id, url, onDomain, onEmail, onSsl, onInvoice);
            TempData["Success"] = $"Webhook added. Signing secret: {endpoint.Secret}";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateWebhookAsync(int id, bool onDomain, bool onEmail,
        bool onSsl, bool onInvoice, bool isActive)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        try
        {
            await _settings.UpdateWebhookAsync(user.Id, id, onDomain, onEmail, onSsl, onInvoice, isActive);
            TempData["Success"] = "Webhook updated.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteWebhookAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        await _settings.DeleteWebhookAsync(user.Id, id);
        TempData["Success"] = "Webhook removed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestWebhookAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        try
        {
            var delivery = await _settings.TestWebhookAsync(user.Id, id);
            if (delivery.Success)
                TempData["Success"] = $"Test delivered — HTTP {delivery.ResponseCode} in {delivery.DurationMs} ms.";
            else
                TempData["Error"] = delivery.ResponseCode == 0
                    ? $"Delivery failed: {delivery.ResponseBody}"
                    : $"Endpoint replied HTTP {delivery.ResponseCode}.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage();
    }
}
