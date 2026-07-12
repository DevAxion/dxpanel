using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Services.Developer;

namespace SRXPanel.Pages.Client.Developer;

public class DnsLookupModel : PageModel
{
    private readonly IDnsLookupService _dns;

    public DnsLookupModel(IDnsLookupService dns) => _dns = dns;

    [BindProperty(SupportsGet = true)] public string? Domain { get; set; }
    [BindProperty(SupportsGet = true)] public DnsRecordKind Type { get; set; } = DnsRecordKind.A;
    [BindProperty(SupportsGet = true)] public bool Propagation { get; set; }

    public DnsLookupResult? Result { get; private set; }
    public string? Error { get; private set; }
    public string? Expected { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Domain)) return;

        try
        {
            Result = Propagation
                ? await _dns.CheckPropagationAsync(Domain, Type, ct)
                : await _dns.LookupAsync(Domain, Type, null, ct);

            Expected = _dns.ExpectedValue(Type);
        }
        catch (InvalidOperationException ex)
        {
            Error = ex.Message;
        }
    }
}
