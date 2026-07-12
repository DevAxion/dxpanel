using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SRXPanel.Pages.Docs;

public class ApiModel : PageModel
{
    /// <summary>Absolute base URL of this panel, used in the copy-pasteable examples.</summary>
    public string BaseUrl => $"{Request.Scheme}://{Request.Host}";

    public string Version => SRXPanel.AppInfo.Version;

    public record ChangelogEntry(string Version, string Date, string[] Changes);

    public static readonly ChangelogEntry[] Changelog =
    {
        new("v1.1", "2026-07-10", new[]
        {
            "Added git deployment webhooks (POST /api/git/webhook/{id}/{secret}).",
            "Added the browser terminal WebSocket endpoint (/ws/terminal/{token}).",
            "Published this OpenAPI document at /openapi/v1.json."
        }),
        new("v1.0", "2026-07-08", new[]
        {
            "REST API with JWT bearer authentication.",
            "WHMCS and Blesta provisioning modules.",
            "Outbound webhooks with HMAC-SHA256 signatures."
        })
    };
}
