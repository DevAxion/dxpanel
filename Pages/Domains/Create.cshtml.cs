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

namespace SRXPanel.Pages.Domains;

public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _auditLog;
    private readonly INginxService _nginx;

    public CreateModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IAuditLogService auditLog,
        INginxService nginx)
    {
        _db = db;
        _userManager = userManager;
        _auditLog = auditLog;
        _nginx = nginx;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool CanChooseOwner { get; set; }
    public List<SelectListItem> OwnerOptions { get; set; } = new();

    public class InputModel
    {
        [Required]
        [StringLength(255)]
        [RegularExpression(@"^(?!-)[A-Za-z0-9-]{1,63}(?<!-)(\.[A-Za-z0-9-]{1,63})*\.[A-Za-z]{2,}$",
            ErrorMessage = "Enter a valid domain name.")]
        [Display(Name = "Domain Name")]
        public string DomainName { get; set; } = string.Empty;

        [Required]
        [StringLength(500)]
        [Display(Name = "Document Root")]
        public string DocumentRoot { get; set; } = string.Empty;

        [Display(Name = "PHP Version")]
        public string PhpVersion { get; set; } = "8.3";

        [Display(Name = "Enable SSL")]
        public bool SslEnabled { get; set; }

        [Display(Name = "Owner")]
        public string? OwnerId { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadOptionsAsync();

        if (string.IsNullOrEmpty(Input.DocumentRoot))
        {
            Input.DocumentRoot = "/home/{user}/public_html";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser == null) return Challenge();

        string ownerId;
        if (User.IsInRole(Roles.Client))
        {
            ownerId = currentUser.Id;
        }
        else
        {
            if (string.IsNullOrEmpty(Input.OwnerId))
            {
                ModelState.AddModelError(nameof(Input.OwnerId), "Please select an owner.");
                await LoadOptionsAsync();
                return Page();
            }
            ownerId = Input.OwnerId;
        }

        var owner = await _userManager.FindByIdAsync(ownerId);
        if (owner == null || !await CanAssignToOwnerAsync(owner))
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

        if (await _db.Domains.AnyAsync(d => d.DomainName == Input.DomainName))
        {
            ModelState.AddModelError(nameof(Input.DomainName), "This domain is already registered.");
            await LoadOptionsAsync();
            return Page();
        }

        var package = owner.PackageId.HasValue ? await _db.Packages.FindAsync(owner.PackageId.Value) : null;
        if (package != null && package.MaxDomains > 0)
        {
            var existingCount = await _db.Domains.CountAsync(d => d.UserId == owner.Id);
            if (existingCount >= package.MaxDomains)
            {
                ModelState.AddModelError(string.Empty, $"Domain limit reached for package '{package.Name}' ({package.MaxDomains} max).");
                await LoadOptionsAsync();
                return Page();
            }
        }

        var domain = new Domain
        {
            UserId = owner.Id,
            DomainName = Input.DomainName.ToLowerInvariant(),
            DocumentRoot = Input.DocumentRoot,
            PhpVersion = Input.PhpVersion,
            SslEnabled = Input.SslEnabled,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Domains.Add(domain);
        await _db.SaveChangesAsync();

        await _auditLog.LogAsync("Create", "Domain", domain.Id.ToString(), domain.DomainName);

        // Provision the nginx virtual host (simulated on Windows/dev).
        var nginxResult = await _nginx.CreateVirtualHostAsync(domain.DomainName, domain.DocumentRoot, domain.PhpVersion);
        var suffix = nginxResult.Simulated ? " (nginx vhost simulated)" : nginxResult.Success ? " nginx vhost provisioned." : $" Warning: nginx provisioning failed — {nginxResult.Message}";

        TempData[nginxResult.Success ? "Success" : "Error"] = $"Domain '{domain.DomainName}' has been added.{suffix}";
        return RedirectToPage("/Domains/Index");
    }

    private async Task<bool> CanAssignToOwnerAsync(ApplicationUser owner)
    {
        if (User.IsInRole(Roles.SuperAdmin)) return true;

        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser == null) return false;

        if (User.IsInRole(Roles.Reseller))
        {
            return owner.Id == currentUser.Id || owner.ResellerId == currentUser.Id;
        }

        return owner.Id == currentUser.Id;
    }

    private async Task LoadOptionsAsync()
    {
        CanChooseOwner = !User.IsInRole(Roles.Client);
        if (!CanChooseOwner) return;

        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser == null) return;

        List<ApplicationUser> owners;
        if (User.IsInRole(Roles.SuperAdmin))
        {
            owners = await _db.Users.ToListAsync();
        }
        else
        {
            owners = await _db.Users.Where(u => u.ResellerId == currentUser.Id).ToListAsync();
        }

        OwnerOptions = owners.Select(u => new SelectListItem(u.UserName, u.Id)).ToList();
    }
}
