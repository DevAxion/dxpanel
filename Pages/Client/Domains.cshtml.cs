using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Interfaces;
using SRXPanel.Services.Reseller;

namespace SRXPanel.Pages.Client;

public class DomainsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _audit;
    private readonly INginxService _nginx;
    private readonly IResourceGuard _guard;

    public DomainsModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        IAuditLogService audit, INginxService nginx, IResourceGuard guard)
    {
        _db = db;
        _userManager = userManager;
        _audit = audit;
        _nginx = nginx;
        _guard = guard;
    }

    public List<Domain> Domains { get; set; } = new();
    public Dictionary<int, List<Subdomain>> Subdomains { get; set; } = new();
    public Dictionary<int, List<DomainRedirect>> Redirects { get; set; } = new();
    public int MaxDomains { get; set; }
    public bool AtLimit { get; set; }
    public static string[] PhpVersions => PanelSettings.PhpVersions;

    [BindProperty]
    public string NewDomain { get; set; } = string.Empty;

    private async Task<ApplicationUser?> LoadAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return null;

        Domains = await _db.Domains.Where(d => d.UserId == user.Id).OrderBy(d => d.DomainName).ToListAsync();
        var domainIds = Domains.Select(d => d.Id).ToList();
        Subdomains = (await _db.Subdomains.Where(s => domainIds.Contains(s.DomainId)).ToListAsync())
            .GroupBy(s => s.DomainId).ToDictionary(g => g.Key, g => g.ToList());
        Redirects = (await _db.DomainRedirects.Where(r => domainIds.Contains(r.DomainId)).ToListAsync())
            .GroupBy(r => r.DomainId).ToDictionary(g => g.Key, g => g.ToList());

        var plan = await GetPlanAsync(user.Id);
        MaxDomains = plan?.MaxDomains ?? 0;
        AtLimit = MaxDomains > 0 && Domains.Count >= MaxDomains;
        return user;
    }

    private async Task<Plan?> GetPlanAsync(string userId)
    {
        var sub = await _db.Subscriptions.Include(s => s.Plan)
            .Where(s => s.UserId == userId && s.Status != SubscriptionStatus.Cancelled)
            .OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync();
        return sub?.Plan;
    }

    private async Task<Domain?> OwnedDomainAsync(int id, string userId) =>
        await _db.Domains.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);

    public async Task<IActionResult> OnGetAsync()
    {
        if (await LoadAsync() == null) return Challenge();
        return Page();
    }

    public async Task<IActionResult> OnPostAddAsync()
    {
        var user = await LoadAsync();
        if (user == null) return Challenge();

        if (AtLimit)
        {
            TempData["Error"] = "You have reached your plan's domain limit. Upgrade to add more.";
            return RedirectToPage();
        }
        var (ok, guardError) = await _guard.CheckAsync(user, ResourceKind.Domain);
        if (!ok)
        {
            TempData["Error"] = guardError;
            return RedirectToPage();
        }
        NewDomain = (NewDomain ?? "").Trim().ToLowerInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(NewDomain, @"^(?!-)[a-z0-9-]{1,63}(?<!-)(\.[a-z0-9-]{1,63})*\.[a-z]{2,}$"))
        {
            TempData["Error"] = "Enter a valid domain name.";
            return RedirectToPage();
        }
        if (await _db.Domains.AnyAsync(d => d.DomainName == NewDomain))
        {
            TempData["Error"] = "That domain is already registered.";
            return RedirectToPage();
        }

        var username = HostingHelpers.UserPrefix(user.UserName ?? "user");
        var domain = new Domain
        {
            UserId = user.Id,
            DomainName = NewDomain,
            DocumentRoot = $"/home/{username}/public_html/{NewDomain}",
            PhpVersion = "8.3",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _db.Domains.Add(domain);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "Domain", domain.Id.ToString(), domain.DomainName);
        await _nginx.CreateVirtualHostAsync(domain.DomainName, domain.DocumentRoot, domain.PhpVersion);
        TempData["Success"] = $"Domain '{domain.DomainName}' added.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var domain = await OwnedDomainAsync(id, user.Id);
        if (domain == null) { TempData["Error"] = "Domain not found."; return RedirectToPage(); }

        var name = domain.DomainName;
        _db.Domains.Remove(domain);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "Domain", id.ToString(), name);
        await _nginx.DeleteVirtualHostAsync(name);
        TempData["Success"] = $"Domain '{name}' removed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSettingsAsync(int id, string phpVersion, bool forceHttps, bool directoryListing, string? error404, string? error500)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var domain = await OwnedDomainAsync(id, user.Id);
        if (domain == null) { TempData["Error"] = "Domain not found."; return RedirectToPage(); }

        domain.PhpVersion = PanelSettings.PhpVersions.Contains(phpVersion) ? phpVersion : domain.PhpVersion;
        domain.ForceHttps = forceHttps;
        domain.DirectoryListing = directoryListing;
        domain.Error404Path = error404;
        domain.Error500Path = error500;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Update", "Domain", id.ToString(), $"settings for {domain.DomainName}");
        // Re-provision the vhost so PHP version / flags take effect.
        await _nginx.CreateVirtualHostAsync(domain.DomainName, domain.DocumentRoot, domain.PhpVersion);
        TempData["Success"] = $"Settings saved for '{domain.DomainName}'.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddSubdomainAsync(int domainId, string name)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var domain = await OwnedDomainAsync(domainId, user.Id);
        if (domain == null) { TempData["Error"] = "Domain not found."; return RedirectToPage(); }

        name = (name ?? "").Trim().ToLowerInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-z0-9-]+$"))
        {
            TempData["Error"] = "Invalid subdomain name.";
            return RedirectToPage();
        }
        if (await _db.Subdomains.AnyAsync(s => s.DomainId == domainId && s.Name == name))
        {
            TempData["Error"] = "That subdomain already exists.";
            return RedirectToPage();
        }
        _db.Subdomains.Add(new Subdomain
        {
            DomainId = domainId,
            UserId = user.Id,
            Name = name,
            DocumentRoot = $"{domain.DocumentRoot}/{name}",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "Subdomain", null, $"{name}.{domain.DomainName}");
        await _nginx.CreateVirtualHostAsync($"{name}.{domain.DomainName}", $"{domain.DocumentRoot}/{name}", domain.PhpVersion);
        TempData["Success"] = $"Subdomain '{name}.{domain.DomainName}' created.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteSubdomainAsync(int subId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var sub = await _db.Subdomains.FirstOrDefaultAsync(s => s.Id == subId && s.UserId == user.Id);
        if (sub == null) { TempData["Error"] = "Not found."; return RedirectToPage(); }
        _db.Subdomains.Remove(sub);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "Subdomain", subId.ToString(), sub.Name);
        TempData["Success"] = "Subdomain removed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddRedirectAsync(int domainId, string source, string target, RedirectType type)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var domain = await OwnedDomainAsync(domainId, user.Id);
        if (domain == null) { TempData["Error"] = "Domain not found."; return RedirectToPage(); }

        if (string.IsNullOrWhiteSpace(target))
        {
            TempData["Error"] = "Redirect target is required.";
            return RedirectToPage();
        }
        _db.DomainRedirects.Add(new DomainRedirect
        {
            DomainId = domainId,
            UserId = user.Id,
            Source = string.IsNullOrWhiteSpace(source) ? "/" : source.Trim(),
            Target = target.Trim(),
            Type = type,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "DomainRedirect", null, $"{domain.DomainName}{source} -> {target}");
        TempData["Success"] = "Redirect added.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteRedirectAsync(int redirectId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var r = await _db.DomainRedirects.FirstOrDefaultAsync(x => x.Id == redirectId && x.UserId == user.Id);
        if (r == null) { TempData["Error"] = "Not found."; return RedirectToPage(); }
        _db.DomainRedirects.Remove(r);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Redirect removed.";
        return RedirectToPage();
    }
}
