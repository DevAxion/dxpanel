using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Interfaces;
using SRXPanel.Services.Reseller;

namespace SRXPanel.Pages.Client;

public class FtpModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISecretHasher _hasher;
    private readonly IAuditLogService _audit;
    private readonly IFtpService _ftp;
    private readonly PanelSettings _panel;
    private readonly IResourceGuard _guard;

    public FtpModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, ISecretHasher hasher,
        IAuditLogService audit, IFtpService ftp, IOptionsMonitor<PanelSettings> panel, IResourceGuard guard)
    {
        _db = db;
        _userManager = userManager;
        _hasher = hasher;
        _audit = audit;
        _ftp = ftp;
        _panel = panel.CurrentValue;
        _guard = guard;
    }

    public List<FtpAccount> Accounts { get; set; } = new();
    public int MaxFtp { get; set; }
    public bool AtLimit { get; set; }
    public string FtpHost => _panel.Hostname;
    public string HomeBase { get; set; } = "/home/user";

    // The last-created account's plaintext password, shown once.
    [TempData] public string? NewPassword { get; set; }
    [TempData] public string? NewUsername { get; set; }

    private async Task<ApplicationUser?> LoadAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return null;
        Accounts = await _db.FtpAccounts.Where(f => f.UserId == user.Id).OrderByDescending(f => f.CreatedAt).ToListAsync();
        HomeBase = $"/home/{HostingHelpers.UserPrefix(user.UserName ?? "user")}";
        var sub = await _db.Subscriptions.Include(s => s.Plan).Where(s => s.UserId == user.Id && s.Status != SubscriptionStatus.Cancelled)
            .OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync();
        MaxFtp = sub?.Plan?.MaxFtpAccounts ?? 0;
        AtLimit = MaxFtp > 0 && Accounts.Count >= MaxFtp;
        return user;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (await LoadAsync() == null) return Challenge();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(string suffix)
    {
        var user = await LoadAsync();
        if (user == null) return Challenge();
        if (AtLimit) { TempData["Error"] = "FTP account limit reached for your plan."; return RedirectToPage(); }
        var (guardOk, guardError) = await _guard.CheckAsync(user, ResourceKind.Ftp);
        if (!guardOk) { TempData["Error"] = guardError; return RedirectToPage(); }

        if (!System.Text.RegularExpressions.Regex.IsMatch(suffix ?? "", "^[a-zA-Z0-9_]{1,32}$"))
        {
            TempData["Error"] = "Invalid username.";
            return RedirectToPage();
        }
        var username = HostingHelpers.Prefixed(user.UserName ?? "user", suffix!);
        if (await _db.FtpAccounts.AnyAsync(f => f.Username == username))
        {
            TempData["Error"] = "That FTP username already exists.";
            return RedirectToPage();
        }
        var password = HostingHelpers.GeneratePassword();
        var home = $"{HomeBase}/public_html";
        _db.FtpAccounts.Add(new FtpAccount
        {
            UserId = user.Id, Username = username, PasswordHash = _hasher.Hash(password),
            HomeDirectory = home, QuotaMB = 0, IsActive = true, CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "FtpAccount", null, username);
        await _ftp.CreateFtpUserAsync(username, password, home);

        NewUsername = username;
        NewPassword = password;
        TempData["Success"] = $"FTP account '{username}' created.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPasswordAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var account = await _db.FtpAccounts.FirstOrDefaultAsync(f => f.Id == id && f.UserId == user.Id);
        if (account == null) { TempData["Error"] = "Not found."; return RedirectToPage(); }

        var password = HostingHelpers.GeneratePassword();
        account.PasswordHash = _hasher.Hash(password);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Update", "FtpAccount", id.ToString(), $"password reset for {account.Username}");
        await _ftp.ChangePasswordAsync(account.Username, password);
        NewUsername = account.Username;
        NewPassword = password;
        TempData["Success"] = "Password reset.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var account = await _db.FtpAccounts.FirstOrDefaultAsync(f => f.Id == id && f.UserId == user.Id);
        if (account == null) { TempData["Error"] = "Not found."; return RedirectToPage(); }
        var name = account.Username;
        _db.FtpAccounts.Remove(account);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "FtpAccount", id.ToString(), name);
        await _ftp.DeleteFtpUserAsync(name);
        TempData["Success"] = $"FTP account '{name}' deleted.";
        return RedirectToPage();
    }
}
