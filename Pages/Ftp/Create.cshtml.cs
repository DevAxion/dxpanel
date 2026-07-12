using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Pages.Ftp;

public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IUserScopeService _scope;
    private readonly ISecretHasher _hasher;
    private readonly IRateLimitService _rateLimit;
    private readonly IAuditLogService _audit;
    private readonly IFtpService _ftp;

    public CreateModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IUserScopeService scope,
        ISecretHasher hasher, IRateLimitService rateLimit, IAuditLogService audit, IFtpService ftp)
    {
        _db = db;
        _userManager = userManager;
        _scope = scope;
        _hasher = hasher;
        _rateLimit = rateLimit;
        _audit = audit;
        _ftp = ftp;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool CanChooseOwner { get; set; }
    public List<SelectListItem> OwnerOptions { get; set; } = new();
    public List<SelectListItem> DomainOptions { get; set; } = new();
    public string Prefix { get; set; } = string.Empty;

    public class InputModel
    {
        [Required]
        [RegularExpression("^[a-zA-Z0-9_]{1,32}$", ErrorMessage = "Use letters, numbers and underscore only.")]
        [Display(Name = "FTP Username")]
        public string UsernameSuffix { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = 8)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [StringLength(500)]
        [Display(Name = "Home Directory")]
        public string HomeDirectory { get; set; } = string.Empty;

        [Display(Name = "Quota (MB, 0 = unlimited)")]
        [Range(0, long.MaxValue)]
        public long QuotaMB { get; set; }

        [Display(Name = "Owner")]
        public string? OwnerId { get; set; }

        [Display(Name = "Associated Domain")]
        public int? DomainId { get; set; }
    }

    public async Task OnGetAsync()
    {
        Input.Password = HostingHelpers.GeneratePassword();
        await LoadOptionsAsync();
        var current = await _userManager.GetUserAsync(User);
        if (current?.UserName != null)
        {
            Input.HomeDirectory = $"/home/{HostingHelpers.UserPrefix(current.UserName)}/public_html";
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var owner = await ResolveOwnerAsync();
        if (owner == null)
        {
            ModelState.AddModelError(string.Empty, "Invalid owner selected.");
            await LoadOptionsAsync();
            return Page();
        }

        if (!ModelState.IsValid)
        {
            await LoadOptionsAsync();
            return Page();
        }

        if (!_rateLimit.IsAllowed(owner.Id, "create"))
        {
            TempData["Error"] = "Rate limit reached. Please wait a minute before creating more resources.";
            return RedirectToPage("/Ftp/Index");
        }

        var package = owner.PackageId.HasValue ? await _db.Packages.FindAsync(owner.PackageId.Value) : null;
        if (package != null && package.MaxFtpAccounts > 0)
        {
            var count = await _db.FtpAccounts.CountAsync(f => f.UserId == owner.Id);
            if (count >= package.MaxFtpAccounts)
            {
                ModelState.AddModelError(string.Empty, $"FTP account limit reached for package '{package.Name}' ({package.MaxFtpAccounts} max).");
                await LoadOptionsAsync();
                return Page();
            }
        }

        var username = HostingHelpers.Prefixed(owner.UserName!, Input.UsernameSuffix);
        if (await _db.FtpAccounts.AnyAsync(f => f.Username == username))
        {
            ModelState.AddModelError(nameof(Input.UsernameSuffix), "An FTP account with this username already exists.");
            await LoadOptionsAsync();
            return Page();
        }

        var account = new FtpAccount
        {
            UserId = owner.Id,
            DomainId = Input.DomainId,
            Username = username,
            PasswordHash = _hasher.Hash(Input.Password),
            HomeDirectory = Input.HomeDirectory,
            QuotaMB = Input.QuotaMB,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.FtpAccounts.Add(account);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "FtpAccount", account.Id.ToString(), username);

        var ftpResult = await _ftp.CreateFtpUserAsync(username, Input.Password, Input.HomeDirectory);
        if (Input.QuotaMB > 0) await _ftp.SetQuotaAsync(username, Input.QuotaMB);
        var suffix = ftpResult.Simulated ? " (vsftpd commands simulated)" : ftpResult.Success ? " vsftpd user created." : $" Warning: vsftpd issue — {ftpResult.Message}";

        TempData["Success"] = $"FTP account '{username}' created.{suffix}";
        return RedirectToPage("/Ftp/Index");
    }

    private async Task<ApplicationUser?> ResolveOwnerAsync()
    {
        var current = await _userManager.GetUserAsync(User);
        if (current == null) return null;
        if (User.IsInRole(Roles.Client)) return current;
        if (string.IsNullOrEmpty(Input.OwnerId)) return null;
        if (!await _scope.CanManageUserAsync(User, Input.OwnerId)) return null;
        return await _userManager.FindByIdAsync(Input.OwnerId);
    }

    private async Task LoadOptionsAsync()
    {
        CanChooseOwner = !User.IsInRole(Roles.Client);
        var current = await _userManager.GetUserAsync(User);
        Prefix = current?.UserName != null ? HostingHelpers.UserPrefix(current.UserName) + "_" : string.Empty;

        if (CanChooseOwner)
        {
            var users = await _scope.GetManageableUsersAsync(User);
            OwnerOptions = users.OrderBy(u => u.UserName).Select(u => new SelectListItem(u.UserName, u.Id)).ToList();
        }

        var ownerId = User.IsInRole(Roles.Client) ? current?.Id : Input.OwnerId;
        if (!string.IsNullOrEmpty(ownerId))
        {
            DomainOptions = await _db.Domains.Where(d => d.UserId == ownerId)
                .Select(d => new SelectListItem(d.DomainName, d.Id.ToString())).ToListAsync();
        }
    }
}
