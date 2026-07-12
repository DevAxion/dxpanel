using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SRXPanel.Pages;

[AllowAnonymous]
public class DomainsModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public record DomainResult(string Domain, string Tld, decimal Price, bool Available);

    public List<DomainResult> Results { get; private set; } = new();

    // TLDs offered with their annual price.
    private static readonly (string Tld, decimal Price)[] Tlds =
    {
        (".com", 12.99m), (".net", 14.99m), (".org", 13.49m),
        (".az", 29.99m), (".io", 39.99m), (".dev", 15.99m)
    };

    public void OnGet()
    {
        if (string.IsNullOrWhiteSpace(Q)) return;

        // Normalize to the SLD (strip any TLD the user typed).
        var sld = Q.Trim().ToLowerInvariant();
        var dot = sld.IndexOf('.');
        if (dot > 0) sld = sld[..dot];
        sld = new string(sld.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        if (string.IsNullOrEmpty(sld)) return;

        // Deterministic pseudo-availability so results are stable per search.
        foreach (var (tld, price) in Tlds)
        {
            var hash = Math.Abs(($"{sld}{tld}").GetHashCode());
            var available = hash % 3 != 0; // ~2/3 available
            Results.Add(new DomainResult(sld + tld, tld, price, available));
        }
    }
}
