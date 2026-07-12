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

namespace SRXPanel.Pages.Email;

public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IUserScopeService _scope;
    private readonly ISecretHasher _hasher;
    private readonly IRateLimitService _rateLimit;
    private readonly IAuditLogService _audit;
    private readonly IEmailService _email;

    public CreateModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IUserScopeService scope,
        ISecretHasher hasher, IRateLimitService rateLimit, IAuditLogService audit, IEmailService email)
    {
        _db = db;
        _userManager = userManager;
        _scope = scope;
        _hasher = hasher;
        _rateLimit = rateLimit;
        _audit = audit;
        _email = email;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool CanChooseOwner { get; set; }
    public List<SelectListItem> OwnerOptions { get; set; } = new();
    public List<SelectListItem> DomainOptions { get; set; } = new();

    public class InputModel
    {
        [Required]
        [RegularExpression("^[a-zA-Z0-9._-]{1,64}$", ErrorMessage = "Invalid mailbox name.")]
        [Display(Name = "Mailbox")]
        public string LocalPart { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Domain")]
        public int DomainId { get; set; }

        [Required]
        [StringLength(100, MinimumLength = 8)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Quota (MB, 0 = unlimited)")]
        [Range(0, long.MaxValue)]
        public long QuotaMB { get; set; } = 1024;

        [Display(Name = "Owner")]
        public string? OwnerId { get; set; }
    }

    public async Task OnGetAsync()
    {
        Input.Password = HostingHelpers.GeneratePassword();
        await LoadOptionsAsync();
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

        var domain = await _db.Domains.FirstOrDefaultAsync(d => d.Id == Input.DomainId && d.UserId == owner.Id);
        if (domain == null)
        {
            ModelState.AddModelError(nameof(Input.DomainId), "Select a domain owned by this user.");
            await LoadOptionsAsync();
            return Page();
        }

        if (!_rateLimit.IsAllowed(owner.Id, "create"))
        {
            TempData["Error"] = "Rate limit reached. Please wait a minute before creating more resources.";
            return RedirectToPage("/Email/Index");
        }

        var package = owner.PackageId.HasValue ? await _db.Packages.FindAsync(owner.PackageId.Value) : null;
        if (package != null && package.MaxEmails > 0)
        {
            var count = await _db.EmailAccounts.CountAsync(e => e.UserId == owner.Id);
            if (count >= package.MaxEmails)
            {
                ModelState.AddModelError(string.Empty, $"Email account limit reached for package '{package.Name}' ({package.MaxEmails} max).");
                await LoadOptionsAsync();
                return Page();
            }
        }

        var address = $"{Input.LocalPart.ToLowerInvariant()}@{domain.DomainName}";
        if (await _db.EmailAccounts.AnyAsync(e => e.EmailAddress == address))
        {
            ModelState.AddModelError(nameof(Input.LocalPart), "This email address already exists.");
            await LoadOptionsAsync();
            return Page();
        }

        var account = new EmailAccount
        {
            UserId = owner.Id,
            DomainId = domain.Id,
            EmailAddress = address,
            PasswordHash = _hasher.Hash(Input.Password),
            QuotaMB = Input.QuotaMB,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.EmailAccounts.Add(account);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "EmailAccount", account.Id.ToString(), address);

        var mailResult = await _email.CreateMailboxAsync(address, Input.Password, Input.QuotaMB);
        var suffix = mailResult.Simulated ? " (Postfix/Dovecot commands simulated)" : mailResult.Success ? " mailbox provisioned." : $" Warning: mail provisioning issue — {mailResult.Message}";

        TempData["Success"] = $"Email account '{address}' created.{suffix}";
        return RedirectToPage("/Email/Index");
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
