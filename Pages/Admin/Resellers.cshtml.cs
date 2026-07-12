using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Reseller;

namespace SRXPanel.Pages.Admin;

public class ResellersModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IResellerService _resellers;
    private readonly IAuditLogService _audit;

    public ResellersModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager, IResellerService resellers, IAuditLogService audit)
    {
        _db = db;
        _userManager = userManager;
        _signInManager = signInManager;
        _resellers = resellers;
        _audit = audit;
    }

    public const string CascadeSuspendReason = "Reseller account suspended";

    public List<Row> Rows { get; set; } = new();
    public List<SelectListItem> ExistingUsers { get; set; } = new();
    public EditRow? Edit { get; set; }
    public List<ApplicationUser> EditClients { get; set; } = new();

    [BindProperty] public InputModel Input { get; set; } = new();

    public class Row
    {
        public ResellerProfile Profile { get; set; } = null!;
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int ClientCount { get; set; }
        public long DiskUsedMB { get; set; }
    }

    public class EditRow
    {
        public ResellerProfile Profile { get; set; } = null!;
        public string UserName { get; set; } = string.Empty;
    }

    public class InputModel
    {
        // Existing user selection OR new-user fields
        public string? ExistingUserId { get; set; }
        [StringLength(50)] public string? NewUserName { get; set; }
        [EmailAddress] public string? NewEmail { get; set; }
        public string? NewPassword { get; set; }

        [Required, StringLength(150)] public string CompanyName { get; set; } = string.Empty;
        [Range(0, long.MaxValue)] public long DiskQuotaMB { get; set; } = 10240;
        [Range(0, long.MaxValue)] public long BandwidthQuotaMB { get; set; } = 102400;
        [Range(0, int.MaxValue)] public int MaxClients { get; set; } = 10;
        [Range(0, int.MaxValue)] public int MaxDomains { get; set; } = 50;
        public bool AllowEmail { get; set; } = true;
        public bool AllowDns { get; set; } = true;
        public bool AllowBackups { get; set; } = true;
        public bool AllowCustomPhp { get; set; } = true;
    }

    private async Task LoadAsync(int? editId = null)
    {
        var profiles = await _db.ResellerProfiles.Include(p => p.User).ToListAsync();
        foreach (var p in profiles)
        {
            var clients = await _db.Users.Where(u => u.ResellerId == p.UserId).ToListAsync();
            var usage = await _resellers.GetUsageAsync(p);
            Rows.Add(new Row
            {
                Profile = p,
                UserName = p.User?.UserName ?? "—",
                Email = p.User?.Email ?? "—",
                ClientCount = clients.Count,
                DiskUsedMB = usage.DiskUsedMB
            });
        }

        // Users eligible to be promoted (not already a reseller, not the admin themselves handled loosely).
        var resellerUserIds = profiles.Select(p => p.UserId).ToHashSet();
        var candidates = await _db.Users
            .Where(u => !resellerUserIds.Contains(u.Id) && u.ResellerId == null)
            .ToListAsync();
        // Only show users currently in the Reseller role or without special role could be promoted; keep it simple: all candidates.
        ExistingUsers = candidates.Select(u => new SelectListItem($"{u.UserName} ({u.Email})", u.Id)).ToList();

        if (editId != null)
        {
            var prof = profiles.FirstOrDefault(p => p.Id == editId);
            if (prof != null)
            {
                Edit = new EditRow { Profile = prof, UserName = prof.User?.UserName ?? "—" };
                EditClients = await _db.Users.Where(u => u.ResellerId == prof.UserId)
                    .OrderByDescending(u => u.CreatedAt).ToListAsync();
            }
        }
    }

    public async Task<IActionResult> OnGetAsync(int? edit = null)
    {
        await LoadAsync(edit);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        ApplicationUser? user;

        if (!string.IsNullOrEmpty(Input.ExistingUserId))
        {
            user = await _userManager.FindByIdAsync(Input.ExistingUserId);
            if (user == null) { TempData["Error"] = "Selected user not found."; return RedirectToPage(); }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Input.NewUserName) || string.IsNullOrWhiteSpace(Input.NewEmail)
                || string.IsNullOrWhiteSpace(Input.NewPassword))
            {
                TempData["Error"] = "Provide username, email and password for the new reseller.";
                return RedirectToPage();
            }
            user = new ApplicationUser
            {
                UserName = Input.NewUserName.Trim(),
                Email = Input.NewEmail.Trim(),
                FullName = Input.CompanyName,
                EmailConfirmed = true,
                IsActive = true,
                DiskQuotaMB = 0,
                BandwidthQuotaMB = 0,
                CreatedAt = DateTime.UtcNow
            };
            var create = await _userManager.CreateAsync(user, Input.NewPassword);
            if (!create.Succeeded)
            {
                TempData["Error"] = string.Join(" ", create.Errors.Select(e => e.Description));
                return RedirectToPage();
            }
        }

        if (await _db.ResellerProfiles.AnyAsync(p => p.UserId == user.Id))
        {
            TempData["Error"] = "That user is already a reseller.";
            return RedirectToPage();
        }

        if (!await _userManager.IsInRoleAsync(user, Roles.Reseller))
        {
            await _userManager.RemoveFromRoleAsync(user, Roles.Client); // in case
            await _userManager.AddToRoleAsync(user, Roles.Reseller);
        }

        _db.ResellerProfiles.Add(new ResellerProfile
        {
            UserId = user.Id,
            CompanyName = Input.CompanyName.Trim(),
            DiskQuotaMB = Input.DiskQuotaMB,
            BandwidthQuotaMB = Input.BandwidthQuotaMB,
            MaxClients = Input.MaxClients,
            MaxDomains = Input.MaxDomains,
            AllowEmail = Input.AllowEmail,
            AllowDns = Input.AllowDns,
            AllowBackups = Input.AllowBackups,
            AllowCustomPhp = Input.AllowCustomPhp,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        _db.ResellerBrandings.Add(new ResellerBranding
        {
            ResellerId = user.Id,
            PanelTitle = Input.CompanyName.Trim()
        });
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "Reseller", user.Id, Input.CompanyName);
        TempData["Success"] = $"Reseller '{user.UserName}' created.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(int profileId)
    {
        var prof = await _db.ResellerProfiles.FirstOrDefaultAsync(p => p.Id == profileId);
        if (prof == null) { TempData["Error"] = "Reseller not found."; return RedirectToPage(); }

        prof.CompanyName = Input.CompanyName.Trim();
        prof.DiskQuotaMB = Input.DiskQuotaMB;
        prof.BandwidthQuotaMB = Input.BandwidthQuotaMB;
        prof.MaxClients = Input.MaxClients;
        prof.MaxDomains = Input.MaxDomains;
        prof.AllowEmail = Input.AllowEmail;
        prof.AllowDns = Input.AllowDns;
        prof.AllowBackups = Input.AllowBackups;
        prof.AllowCustomPhp = Input.AllowCustomPhp;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Update", "Reseller", prof.UserId, prof.CompanyName);
        TempData["Success"] = "Reseller quotas updated.";
        return RedirectToPage(new { edit = profileId });
    }

    public async Task<IActionResult> OnPostSuspendAsync(int profileId)
    {
        var prof = await _db.ResellerProfiles.FirstOrDefaultAsync(p => p.Id == profileId);
        if (prof == null) { TempData["Error"] = "Reseller not found."; return RedirectToPage(); }

        prof.IsActive = !prof.IsActive;
        var clients = await _db.Users.Where(u => u.ResellerId == prof.UserId).ToListAsync();

        if (!prof.IsActive)
        {
            // Cascade-suspend all clients.
            foreach (var c in clients.Where(c => c.IsActive))
            {
                c.IsActive = false;
                c.SuspensionReason = CascadeSuspendReason;
            }
        }
        else
        {
            // Reactivate only clients suspended by the cascade.
            foreach (var c in clients.Where(c => !c.IsActive && c.SuspensionReason == CascadeSuspendReason))
            {
                c.IsActive = true;
                c.SuspensionReason = null;
            }
        }

        // Also reflect on the reseller login account.
        var ru = await _userManager.FindByIdAsync(prof.UserId);
        if (ru != null) { ru.IsActive = prof.IsActive; await _userManager.UpdateAsync(ru); }

        await _db.SaveChangesAsync();
        await _audit.LogAsync(prof.IsActive ? "Unsuspend" : "Suspend", "Reseller", prof.UserId, prof.CompanyName);
        TempData["Success"] = $"Reseller '{prof.CompanyName}' {(prof.IsActive ? "reactivated" : "suspended")} " +
                              $"({clients.Count} client(s) affected).";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostImpersonateAsync(string userId)
    {
        var admin = await _userManager.GetUserAsync(User);
        var reseller = await _userManager.FindByIdAsync(userId);
        if (admin == null || reseller == null) { TempData["Error"] = "User not found."; return RedirectToPage(); }

        _db.ImpersonationSessions.Add(new ImpersonationSession
        {
            ImpersonatorId = admin.Id,
            ImpersonatorName = admin.UserName ?? "admin",
            TargetUserId = reseller.Id,
            TargetUserName = reseller.UserName ?? "reseller",
            StartedAt = DateTime.UtcNow,
            IsActive = true
        });
        await _db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new("OriginalAdminId", admin.Id),
            new("OriginalAdminName", admin.UserName ?? "admin")
        };
        await _signInManager.SignInWithClaimsAsync(reseller, isPersistent: false, claims);
        await _audit.LogAsync("Impersonate", "Reseller", reseller.Id, $"{admin.UserName} → {reseller.UserName}");
        return RedirectToPage("/Reseller/Dashboard");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int profileId)
    {
        var prof = await _db.ResellerProfiles.Include(p => p.User).FirstOrDefaultAsync(p => p.Id == profileId);
        if (prof == null) { TempData["Error"] = "Reseller not found."; return RedirectToPage(); }

        // Cascade: delete all clients (removes their hosting data), then the reseller user.
        var clients = await _db.Users.Where(u => u.ResellerId == prof.UserId).ToListAsync();
        foreach (var c in clients)
        {
            await _userManager.DeleteAsync(c);
        }

        var name = prof.CompanyName;
        if (prof.User != null)
        {
            await _userManager.DeleteAsync(prof.User); // cascades profile, branding, packages
        }
        else
        {
            _db.ResellerProfiles.Remove(prof);
            await _db.SaveChangesAsync();
        }
        await _audit.LogAsync("Delete", "Reseller", prof.UserId, $"{name} (+{clients.Count} clients)");
        TempData["Success"] = $"Reseller '{name}' and {clients.Count} client(s) deleted.";
        return RedirectToPage();
    }
}
