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

public class ForwardersModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IUserScopeService _scope;
    private readonly IRateLimitService _rateLimit;
    private readonly IAuditLogService _audit;
    private readonly IEmailService _email;

    public ForwardersModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IUserScopeService scope,
        IRateLimitService rateLimit, IAuditLogService audit, IEmailService email)
    {
        _db = db;
        _userManager = userManager;
        _scope = scope;
        _rateLimit = rateLimit;
        _audit = audit;
        _email = email;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<EmailForwarder> Forwarders { get; set; } = new();
    public bool CanChooseOwner { get; set; }
    public List<SelectListItem> OwnerOptions { get; set; } = new();
    public bool ShowOwner { get; set; }

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Source { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Destination { get; set; } = string.Empty;

        [Display(Name = "Owner")]
        public string? OwnerId { get; set; }
    }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var owner = await ResolveOwnerAsync();
        if (owner == null)
        {
            ModelState.AddModelError(string.Empty, "Invalid owner selected.");
            await LoadAsync();
            return Page();
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        if (!_rateLimit.IsAllowed(owner.Id, "create"))
        {
            TempData["Error"] = "Rate limit reached. Please wait a minute before creating more resources.";
            return RedirectToPage();
        }

        var source = Input.Source.ToLowerInvariant();
        var destination = Input.Destination.ToLowerInvariant();
        _db.EmailForwarders.Add(new EmailForwarder
        {
            UserId = owner.Id,
            Source = source,
            Destination = destination,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "EmailForwarder", null, $"{Input.Source} -> {Input.Destination}");

        var fwdResult = await _email.CreateForwarderAsync(source, destination);
        var suffix = fwdResult.Simulated ? " (Postfix commands simulated)" : " Postfix virtual map updated.";
        TempData["Success"] = $"Forwarder created.{suffix}";
        return RedirectToPage();
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

    private async Task LoadAsync()
    {
        ShowOwner = User.IsInRole(Roles.SuperAdmin) || User.IsInRole(Roles.Reseller);
        CanChooseOwner = ShowOwner;
        var manageable = await _scope.GetManageableUserIdsAsync(User);
        Forwarders = await _db.EmailForwarders.Include(f => f.User)
            .Where(f => manageable.Contains(f.UserId))
            .OrderByDescending(f => f.CreatedAt).ToListAsync();

        if (CanChooseOwner)
        {
            var users = await _scope.GetManageableUsersAsync(User);
            OwnerOptions = users.OrderBy(u => u.UserName).Select(u => new SelectListItem(u.UserName, u.Id)).ToList();
        }
    }
}
