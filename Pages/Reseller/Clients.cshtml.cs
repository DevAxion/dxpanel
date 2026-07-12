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
using SRXPanel.Services.Billing;
using SRXPanel.Services.Reseller;

namespace SRXPanel.Pages.Reseller;

public class ClientsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IResellerService _resellers;
    private readonly IAuditLogService _audit;
    private readonly INotificationService _notifications;
    private readonly IMailerService _mailer;

    public ClientsModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager, IResellerService resellers,
        IAuditLogService audit, INotificationService notifications, IMailerService mailer)
    {
        _db = db;
        _userManager = userManager;
        _signInManager = signInManager;
        _resellers = resellers;
        _audit = audit;
        _notifications = notifications;
        _mailer = mailer;
    }

    public ResellerProfile? Profile { get; set; }
    public ResellerUsage Usage { get; set; } = new();
    public List<ApplicationUser> Clients { get; set; } = new();
    public List<SelectListItem> PackageOptions { get; set; } = new();

    public ClientDetail? Detail { get; set; }

    [BindProperty] public InputModel Input { get; set; } = new();

    public class ClientDetail
    {
        public ApplicationUser User { get; set; } = null!;
        public int Domains { get; set; }
        public int Databases { get; set; }
        public int Emails { get; set; }
        public int OpenTickets { get; set; }
    }

    public class InputModel
    {
        [Required, StringLength(50, MinimumLength = 3)] public string UserName { get; set; } = string.Empty;
        [Required, StringLength(150)] public string FullName { get; set; } = string.Empty;
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required, DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
        public int? ResellerPackageId { get; set; }
        [Range(0, long.MaxValue)] public long DiskQuotaMB { get; set; } = 1024;
        [Range(0, long.MaxValue)] public long BandwidthQuotaMB { get; set; } = 10240;
        public bool SendWelcomeEmail { get; set; } = true;
    }

    private async Task<ApplicationUser?> LoadAsync(string? detailId = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return null;
        Profile = await _resellers.GetProfileAsync(user.Id);
        if (Profile != null) Usage = await _resellers.GetUsageAsync(Profile);
        Clients = await _resellers.GetClientsAsync(user.Id);
        PackageOptions = await _db.ResellerPackages
            .Where(p => p.ResellerId == user.Id && p.IsActive)
            .Select(p => new SelectListItem($"{p.Name} — {p.DiskQuotaMB}MB / {p.MaxDomains} domains", p.Id.ToString()))
            .ToListAsync();

        if (detailId != null)
        {
            var c = Clients.FirstOrDefault(x => x.Id == detailId);
            if (c != null)
            {
                Detail = new ClientDetail
                {
                    User = c,
                    Domains = await _db.Domains.CountAsync(d => d.UserId == c.Id),
                    Databases = await _db.Databases.CountAsync(d => d.UserId == c.Id),
                    Emails = await _db.EmailAccounts.CountAsync(e => e.UserId == c.Id),
                    OpenTickets = await _db.Tickets.CountAsync(t => t.UserId == c.Id && t.Status != TicketStatus.Closed)
                };
            }
        }
        return user;
    }

    public async Task<IActionResult> OnGetAsync(string? details = null)
    {
        if (await LoadAsync(details) == null) return Challenge();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var reseller = await LoadAsync();
        if (reseller == null) return Challenge();
        if (Profile == null) { TempData["Error"] = "No reseller allocation configured."; return RedirectToPage(); }

        if (!ModelState.IsValid) { return Page(); }

        // Resolve limits from selected package (overridable) else the entered values.
        var package = Input.ResellerPackageId.HasValue
            ? await _db.ResellerPackages.FirstOrDefaultAsync(p => p.Id == Input.ResellerPackageId && p.ResellerId == reseller.Id)
            : null;

        var disk = package?.DiskQuotaMB ?? Input.DiskQuotaMB;
        var bandwidth = package?.BandwidthQuotaMB ?? Input.BandwidthQuotaMB;
        var maxDomains = package?.MaxDomains ?? 0;

        var (ok, error) = await _resellers.ValidateNewClientAsync(Profile, disk, maxDomains);
        if (!ok) { TempData["Error"] = error; return RedirectToPage(); }

        var client = new ApplicationUser
        {
            UserName = Input.UserName.Trim(),
            Email = Input.Email.Trim(),
            FullName = Input.FullName.Trim(),
            EmailConfirmed = true,
            IsActive = true,
            ResellerId = reseller.Id,
            ResellerPackageId = package?.Id,
            DiskQuotaMB = disk,
            BandwidthQuotaMB = bandwidth,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(client, Input.Password);
        if (!result.Succeeded)
        {
            TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
            return RedirectToPage();
        }
        await _userManager.AddToRoleAsync(client, Roles.Client);
        await _audit.LogAsync("Create", "Client", client.Id, $"{client.UserName} (reseller {reseller.UserName})");

        await _notifications.NotifyAsync(client.Id, "Welcome",
            $"Your hosting account has been created by {Profile.CompanyName}.", NotificationType.Success);

        if (Input.SendWelcomeEmail)
        {
            await _mailer.SendTemplateAsync(client.Email!, "Welcome to your hosting account", "welcome",
                new Dictionary<string, string>
                {
                    ["UserName"] = client.UserName!,
                    ["CompanyName"] = Profile.CompanyName
                });
        }

        TempData["Success"] = $"Client '{client.UserName}' created.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSuspendAsync(string id, string? reason)
    {
        var reseller = await _userManager.GetUserAsync(User);
        if (reseller == null) return Challenge();
        var client = await OwnedClientAsync(id, reseller.Id);
        if (client == null) { TempData["Error"] = "Client not found."; return RedirectToPage(); }

        client.IsActive = !client.IsActive;
        client.SuspensionReason = client.IsActive ? null : (reason ?? "Suspended by reseller");
        await _userManager.UpdateAsync(client);

        await _notifications.NotifyAsync(client.Id,
            client.IsActive ? "Account reactivated" : "Account suspended",
            client.IsActive ? "Your hosting account has been reactivated."
                            : $"Your hosting account has been suspended. {client.SuspensionReason}",
            client.IsActive ? NotificationType.Success : NotificationType.Error);

        await _audit.LogAsync(client.IsActive ? "Unsuspend" : "Suspend", "Client", client.Id, client.SuspensionReason);
        TempData["Success"] = $"Client '{client.UserName}' {(client.IsActive ? "reactivated" : "suspended")}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id)
    {
        var reseller = await _userManager.GetUserAsync(User);
        if (reseller == null) return Challenge();
        var client = await OwnedClientAsync(id, reseller.Id);
        if (client == null) { TempData["Error"] = "Client not found."; return RedirectToPage(); }

        var name = client.UserName;
        var result = await _userManager.DeleteAsync(client);
        if (result.Succeeded)
        {
            await _audit.LogAsync("Delete", "Client", id, name);
            TempData["Success"] = $"Client '{name}' and all associated data deleted.";
        }
        else
        {
            TempData["Error"] = "Failed to delete client.";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostImpersonateAsync(string id)
    {
        var reseller = await _userManager.GetUserAsync(User);
        if (reseller == null) return Challenge();
        var client = await OwnedClientAsync(id, reseller.Id);
        if (client == null) { TempData["Error"] = "Client not found."; return RedirectToPage(); }

        _db.ImpersonationSessions.Add(new ImpersonationSession
        {
            ImpersonatorId = reseller.Id,
            ImpersonatorName = reseller.UserName ?? "reseller",
            TargetUserId = client.Id,
            TargetUserName = client.UserName ?? "client",
            StartedAt = DateTime.UtcNow,
            IsActive = true
        });
        await _db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new("OriginalAdminId", reseller.Id),
            new("OriginalAdminName", reseller.UserName ?? "reseller")
        };
        await _signInManager.SignInWithClaimsAsync(client, isPersistent: false, claims);
        await _audit.LogAsync("Impersonate", "Client", client.Id, $"{reseller.UserName} → {client.UserName}");
        return RedirectToPage("/Dashboard/Index");
    }

    private Task<ApplicationUser?> OwnedClientAsync(string id, string resellerId) =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.ResellerId == resellerId);
}
