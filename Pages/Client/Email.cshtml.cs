using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Interfaces;
using SRXPanel.Services.Reseller;

namespace SRXPanel.Pages.Client;

public class EmailModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISecretHasher _hasher;
    private readonly IAuditLogService _audit;
    private readonly IEmailService _email;
    private readonly IResourceGuard _guard;

    public EmailModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, ISecretHasher hasher,
        IAuditLogService audit, IEmailService email, IResourceGuard guard)
    {
        _db = db;
        _userManager = userManager;
        _hasher = hasher;
        _audit = audit;
        _email = email;
        _guard = guard;
    }

    public List<EmailAccount> Accounts { get; set; } = new();
    public List<EmailForwarder> Forwarders { get; set; } = new();
    public List<SelectListItem> DomainOptions { get; set; } = new();
    public int MaxEmails { get; set; }
    public bool AtLimit { get; set; }
    [TempData] public string? NewPassword { get; set; }
    [TempData] public string? NewEmail { get; set; }

    private async Task<ApplicationUser?> LoadAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return null;
        Accounts = await _db.EmailAccounts.Where(e => e.UserId == user.Id).OrderByDescending(e => e.CreatedAt).ToListAsync();
        Forwarders = await _db.EmailForwarders.Where(f => f.UserId == user.Id).OrderByDescending(f => f.CreatedAt).ToListAsync();
        DomainOptions = await _db.Domains.Where(d => d.UserId == user.Id)
            .Select(d => new SelectListItem(d.DomainName, d.Id.ToString())).ToListAsync();
        var sub = await _db.Subscriptions.Include(s => s.Plan).Where(s => s.UserId == user.Id && s.Status != SubscriptionStatus.Cancelled)
            .OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync();
        MaxEmails = sub?.Plan?.MaxEmails ?? 0;
        AtLimit = MaxEmails > 0 && Accounts.Count >= MaxEmails;
        return user;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (await LoadAsync() == null) return Challenge();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(string localPart, int domainId)
    {
        var user = await LoadAsync();
        if (user == null) return Challenge();
        if (AtLimit) { TempData["Error"] = "Email account limit reached for your plan."; return RedirectToPage(); }
        var (guardOk, guardError) = await _guard.CheckAsync(user, ResourceKind.Email);
        if (!guardOk) { TempData["Error"] = guardError; return RedirectToPage(); }
        var domain = await _db.Domains.FirstOrDefaultAsync(d => d.Id == domainId && d.UserId == user.Id);
        if (domain == null) { TempData["Error"] = "Select one of your domains."; return RedirectToPage(); }
        if (!System.Text.RegularExpressions.Regex.IsMatch(localPart ?? "", "^[a-zA-Z0-9._-]{1,64}$"))
        {
            TempData["Error"] = "Invalid mailbox name.";
            return RedirectToPage();
        }
        var address = $"{localPart!.ToLowerInvariant()}@{domain.DomainName}";
        if (await _db.EmailAccounts.AnyAsync(e => e.EmailAddress == address))
        {
            TempData["Error"] = "That email address already exists.";
            return RedirectToPage();
        }
        var password = HostingHelpers.GeneratePassword();
        _db.EmailAccounts.Add(new EmailAccount
        {
            UserId = user.Id, DomainId = domain.Id, EmailAddress = address, PasswordHash = _hasher.Hash(password),
            QuotaMB = 1024, IsActive = true, CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "EmailAccount", null, address);
        await _email.CreateMailboxAsync(address, password, 1024);
        NewEmail = address;
        NewPassword = password;
        TempData["Success"] = $"Email '{address}' created.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(int id, long quotaMB, bool autoresponder, string? message, double spamThreshold)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var acc = await _db.EmailAccounts.FirstOrDefaultAsync(e => e.Id == id && e.UserId == user.Id);
        if (acc == null) { TempData["Error"] = "Not found."; return RedirectToPage(); }
        acc.QuotaMB = quotaMB;
        acc.AutoresponderEnabled = autoresponder;
        acc.AutoresponderMessage = message;
        acc.SpamThreshold = Math.Clamp(spamThreshold, 1, 15);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Update", "EmailAccount", id.ToString(), acc.EmailAddress);
        TempData["Success"] = $"Settings saved for {acc.EmailAddress}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPasswordAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var acc = await _db.EmailAccounts.FirstOrDefaultAsync(e => e.Id == id && e.UserId == user.Id);
        if (acc == null) { TempData["Error"] = "Not found."; return RedirectToPage(); }
        var password = HostingHelpers.GeneratePassword();
        acc.PasswordHash = _hasher.Hash(password);
        await _db.SaveChangesAsync();
        NewEmail = acc.EmailAddress;
        NewPassword = password;
        TempData["Success"] = "Password reset.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var acc = await _db.EmailAccounts.FirstOrDefaultAsync(e => e.Id == id && e.UserId == user.Id);
        if (acc == null) { TempData["Error"] = "Not found."; return RedirectToPage(); }
        var name = acc.EmailAddress;
        _db.EmailAccounts.Remove(acc);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "EmailAccount", id.ToString(), name);
        await _email.DeleteMailboxAsync(name);
        TempData["Success"] = $"Email '{name}' deleted.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddForwarderAsync(string source, string destination)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
        {
            TempData["Error"] = "Source and destination are required.";
            return RedirectToPage();
        }
        _db.EmailForwarders.Add(new EmailForwarder
        {
            UserId = user.Id, Source = source.Trim().ToLowerInvariant(), Destination = destination.Trim().ToLowerInvariant(),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        await _email.CreateForwarderAsync(source.Trim(), destination.Trim());
        TempData["Success"] = "Forwarder added.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteForwarderAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var f = await _db.EmailForwarders.FirstOrDefaultAsync(x => x.Id == id && x.UserId == user.Id);
        if (f == null) { TempData["Error"] = "Not found."; return RedirectToPage(); }
        var src = f.Source;
        _db.EmailForwarders.Remove(f);
        await _db.SaveChangesAsync();
        await _email.DeleteForwarderAsync(src);
        TempData["Success"] = "Forwarder removed.";
        return RedirectToPage();
    }
}
