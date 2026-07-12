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

namespace SRXPanel.Pages.Databases;

public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IUserScopeService _scope;
    private readonly ISecretHasher _hasher;
    private readonly IRateLimitService _rateLimit;
    private readonly IAuditLogService _audit;
    private readonly IMySqlService _mysql;

    public CreateModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IUserScopeService scope,
        ISecretHasher hasher, IRateLimitService rateLimit, IAuditLogService audit, IMySqlService mysql)
    {
        _db = db;
        _userManager = userManager;
        _scope = scope;
        _hasher = hasher;
        _rateLimit = rateLimit;
        _audit = audit;
        _mysql = mysql;
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
        [Display(Name = "Database Name")]
        public string DbNameSuffix { get; set; } = string.Empty;

        [Required]
        [RegularExpression("^[a-zA-Z0-9_]{1,32}$", ErrorMessage = "Use letters, numbers and underscore only.")]
        [Display(Name = "Database User")]
        public string DbUserSuffix { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = 8)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Owner")]
        public string? OwnerId { get; set; }

        [Display(Name = "Associated Domain")]
        public int? DomainId { get; set; }
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

        if (!_rateLimit.IsAllowed(owner.Id, "create"))
        {
            TempData["Error"] = "Rate limit reached. Please wait a minute before creating more resources.";
            return RedirectToPage("/Databases/Index");
        }

        // Enforce package database limit
        var package = owner.PackageId.HasValue ? await _db.Packages.FindAsync(owner.PackageId.Value) : null;
        if (package != null && package.MaxDatabases > 0)
        {
            var count = await _db.Databases.CountAsync(d => d.UserId == owner.Id);
            if (count >= package.MaxDatabases)
            {
                ModelState.AddModelError(string.Empty, $"Database limit reached for package '{package.Name}' ({package.MaxDatabases} max).");
                await LoadOptionsAsync();
                return Page();
            }
        }

        var dbName = HostingHelpers.Prefixed(owner.UserName!, Input.DbNameSuffix);
        var dbUser = HostingHelpers.Prefixed(owner.UserName!, Input.DbUserSuffix);

        if (await _db.Databases.AnyAsync(d => d.DbName == dbName))
        {
            ModelState.AddModelError(nameof(Input.DbNameSuffix), "A database with this name already exists.");
            await LoadOptionsAsync();
            return Page();
        }

        var database = new Database
        {
            UserId = owner.Id,
            DomainId = Input.DomainId,
            DbName = dbName,
            DbUser = dbUser,
            DbPasswordHash = _hasher.Hash(Input.Password),
            DbSize = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Databases.Add(database);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "Database", database.Id.ToString(), dbName);

        // Provision on the real MySQL server (simulated on Windows/dev).
        await _mysql.CreateDatabaseAsync(dbName);
        await _mysql.CreateUserAsync(dbUser, Input.Password);
        var grant = await _mysql.GrantPermissionsAsync(dbName, dbUser);
        var suffix = grant.Simulated ? " (MySQL commands simulated)" : grant.Success ? " MySQL database provisioned." : $" Warning: MySQL provisioning issue — {grant.Message}";

        TempData["Success"] = $"Database '{dbName}' created with user '{dbUser}'.{suffix}";
        return RedirectToPage("/Databases/Index");
    }

    private async Task<ApplicationUser?> ResolveOwnerAsync()
    {
        var current = await _userManager.GetUserAsync(User);
        if (current == null) return null;

        if (User.IsInRole(Roles.Client))
        {
            return current;
        }

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
            OwnerOptions = users.OrderBy(u => u.UserName)
                .Select(u => new SelectListItem(u.UserName, u.Id)).ToList();
        }

        var ownerId = User.IsInRole(Roles.Client) ? current?.Id : Input.OwnerId;
        if (!string.IsNullOrEmpty(ownerId))
        {
            DomainOptions = await _db.Domains.Where(d => d.UserId == ownerId)
                .Select(d => new SelectListItem(d.DomainName, d.Id.ToString()))
                .ToListAsync();
        }
    }
}
