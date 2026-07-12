using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;

namespace SRXPanel.Pages.Email;

public class EditModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserScopeService _scope;
    private readonly ISecretHasher _hasher;
    private readonly IAuditLogService _audit;

    public EditModel(ApplicationDbContext db, IUserScopeService scope, ISecretHasher hasher, IAuditLogService audit)
    {
        _db = db;
        _scope = scope;
        _hasher = hasher;
        _audit = audit;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string EmailAddress { get; set; } = string.Empty;

    public class InputModel
    {
        [Display(Name = "New Password (leave blank to keep current)")]
        [DataType(DataType.Password)]
        public string? NewPassword { get; set; }

        [Display(Name = "Quota (MB, 0 = unlimited)")]
        [Range(0, long.MaxValue)]
        public long QuotaMB { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var account = await _db.EmailAccounts.FindAsync(Id);
        if (account == null || !await _scope.CanManageUserAsync(User, account.UserId))
        {
            return NotFound();
        }

        EmailAddress = account.EmailAddress;
        Input.QuotaMB = account.QuotaMB;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var account = await _db.EmailAccounts.FindAsync(Id);
        if (account == null || !await _scope.CanManageUserAsync(User, account.UserId))
        {
            return NotFound();
        }

        EmailAddress = account.EmailAddress;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        account.QuotaMB = Input.QuotaMB;

        if (!string.IsNullOrWhiteSpace(Input.NewPassword))
        {
            if (Input.NewPassword.Length < 8)
            {
                ModelState.AddModelError(nameof(Input.NewPassword), "Password must be at least 8 characters.");
                return Page();
            }
            account.PasswordHash = _hasher.Hash(Input.NewPassword);
        }

        await _db.SaveChangesAsync();
        await _audit.LogAsync("Update", "EmailAccount", account.Id.ToString(), account.EmailAddress);

        TempData["Success"] = $"Email account '{account.EmailAddress}' updated.";
        return RedirectToPage("/Email/Index");
    }
}
