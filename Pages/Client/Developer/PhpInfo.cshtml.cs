using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Developer;

namespace SRXPanel.Pages.Client.Developer;

public class PhpInfoModel : PageModel
{
    private readonly IPhpConfigService _php;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _auditLog;

    public PhpInfoModel(IPhpConfigService php, ApplicationDbContext db,
        UserManager<ApplicationUser> userManager, IAuditLogService auditLog)
    {
        _php = php;
        _db = db;
        _userManager = userManager;
        _auditLog = auditLog;
    }

    [BindProperty(SupportsGet = true)] public int? DomainId { get; set; }

    public List<Domain> Domains { get; private set; } = new();
    public Domain? Domain { get; private set; }
    public PhpConfig Config { get; private set; } = new();
    public List<PhpDirective> Directives { get; private set; } = new();
    public IReadOnlyList<PhpExtension> Extensions { get; private set; } = Array.Empty<PhpExtension>();
    public Dictionary<string, List<PhpDirective>> PhpInfo { get; private set; } = new();

    public string[] Timezones => PhpConfigService.Timezones;
    public string[] ErrorLevels => PhpConfigService.ErrorReportingLevels;

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        Domains = await _db.Domains.Where(d => d.UserId == user.Id).OrderBy(d => d.DomainName).ToListAsync();
        if (!Domains.Any()) return Page();

        DomainId ??= Domains.First().Id;
        Domain = Domains.FirstOrDefault(d => d.Id == DomainId);
        if (Domain == null) return NotFound();

        Config = await _php.GetAsync(user.Id, Domain.Id);
        Directives = await _php.GetDirectivesAsync(user.Id, Domain.Id);
        Extensions = _php.GetExtensions(Domain.PhpVersion);
        PhpInfo = await _php.GetPhpInfoAsync(user.Id, Domain.Id);

        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(int domainId, string memoryLimit, int maxExecutionTime,
        string uploadMaxFilesize, string postMaxSize, int maxInputVars, string timezone,
        bool displayErrors, string errorReporting)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        try
        {
            var result = await _php.SaveAsync(user.Id, domainId, new PhpConfig
            {
                MemoryLimit = memoryLimit,
                MaxExecutionTime = maxExecutionTime,
                UploadMaxFilesize = uploadMaxFilesize,
                PostMaxSize = postMaxSize,
                MaxInputVars = maxInputVars,
                Timezone = timezone,
                DisplayErrors = displayErrors,
                ErrorReporting = errorReporting
            });

            await _auditLog.LogAsync("Update", "PhpConfig", domainId.ToString(), memoryLimit);
            TempData["Success"] = result.Message + (result.Simulated ? " (simulated)" : "");
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage(new { domainId });
    }
}
