using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Services.Reseller;

/// <summary>
/// The resolved white-label appearance for the current request. Falls back to
/// the default SRXPanel brand when no reseller context is detected.
/// </summary>
public class BrandingInfo
{
    public string? ResellerId { get; set; }

    // Default brand renders as "SRX" + accent("Panel").
    public string BrandName { get; set; } = "SRX";
    public string BrandSuffix { get; set; } = "Panel";
    public string PanelTitle { get; set; } = "SRXPanel";

    public string? LogoPath { get; set; }
    public string? FaviconPath { get; set; }

    public string PrimaryColor { get; set; } = "#2563eb";
    public string SecondaryColor { get; set; } = "#151a23";
    public string AccentColor { get; set; } = "#3b82f6";

    public string? LoginBackground { get; set; }
    public string? FooterText { get; set; }

    public bool IsWhiteLabel => ResellerId != null;

    public static BrandingInfo Default => new();

    public static BrandingInfo FromEntity(ResellerBranding b) => new()
    {
        ResellerId = b.ResellerId,
        PanelTitle = string.IsNullOrWhiteSpace(b.PanelTitle) ? "Hosting Panel" : b.PanelTitle,
        BrandName = string.IsNullOrWhiteSpace(b.PanelTitle) ? "Hosting Panel" : b.PanelTitle,
        BrandSuffix = "",
        LogoPath = b.LogoPath,
        FaviconPath = b.FaviconPath,
        PrimaryColor = b.PrimaryColor,
        SecondaryColor = b.SecondaryColor,
        AccentColor = b.AccentColor,
        LoginBackground = b.LoginBackground,
        FooterText = b.FooterText
    };
}

public interface IBrandingResolver
{
    Task<BrandingInfo> ResolveAsync(HttpContext context);
}

public class BrandingResolver : IBrandingResolver
{
    private readonly ApplicationDbContext _db;

    public BrandingResolver(ApplicationDbContext db) => _db = db;

    public async Task<BrandingInfo> ResolveAsync(HttpContext context)
    {
        string? resellerId = null;

        // 1) Authenticated user determines branding: a reseller sees their own
        //    brand; a client sees their reseller's brand.
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId != null)
            {
                var user = await _db.Users.AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => new { u.Id, u.ResellerId })
                    .FirstOrDefaultAsync();

                if (user != null)
                {
                    var isReseller = await _db.ResellerProfiles.AnyAsync(p => p.UserId == user.Id);
                    resellerId = isReseller ? user.Id : user.ResellerId;
                }
            }
        }

        // 2) Otherwise (e.g. anonymous login page) match by custom domain / subdomain.
        if (resellerId == null)
        {
            var host = context.Request.Host.Host;
            var byDomain = await _db.ResellerBrandings.AsNoTracking()
                .FirstOrDefaultAsync(b => b.CustomDomain != null && b.CustomDomain == host);
            if (byDomain != null) return BrandingInfo.FromEntity(byDomain);
        }

        if (resellerId != null)
        {
            var branding = await _db.ResellerBrandings.AsNoTracking()
                .FirstOrDefaultAsync(b => b.ResellerId == resellerId);
            if (branding != null) return BrandingInfo.FromEntity(branding);
        }

        return BrandingInfo.Default;
    }
}

public class BrandingMiddleware
{
    public const string ItemKey = "srx.branding";
    private readonly RequestDelegate _next;

    public BrandingMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IBrandingResolver resolver)
    {
        // Skip static assets and API endpoints for performance.
        var path = context.Request.Path.Value ?? string.Empty;
        if (!path.StartsWith("/lib") && !path.StartsWith("/css") && !path.StartsWith("/js") &&
            !path.StartsWith("/branding") && !path.StartsWith("/api"))
        {
            context.Items[ItemKey] = await resolver.ResolveAsync(context);
        }
        await _next(context);
    }
}

public static class BrandingExtensions
{
    public static BrandingInfo Branding(this HttpContext context) =>
        context.Items[BrandingMiddleware.ItemKey] as BrandingInfo ?? BrandingInfo.Default;
}
