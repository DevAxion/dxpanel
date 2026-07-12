using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Pages.Ftp;

public class EditModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserScopeService _scope;
    private readonly ISecretHasher _hasher;
    private readonly IAuditLogService _audit;
    private readonly IFtpService _ftp;

    public EditModel(ApplicationDbContext db, IUserScopeService scope, ISecretHasher hasher, IAuditLogService audit,
        IFtpService ftp)
    {
        _db = db;
        _scope = scope;
        _hasher = hasher;
        _audit = audit;
        _ftp = ftp;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string Username { get; set; } = string.Empty;

    public class InputModel
    {
        [Display(Name = "New Password (leave blank to keep current)")]
        [DataType(DataType.Password)]
        public string? NewPassword { get; set; }

        [Required]
        [StringLength(500)]
        [Display(Name = "Home Directory")]
        public string HomeDirectory { get; set; } = string.Empty;

        [Display(Name = "Quota (MB, 0 = unlimited)")]
        [Range(0, long.MaxValue)]
        public long QuotaMB { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var account = await _db.FtpAccounts.FindAsync(Id);
        if (account == null || !await _scope.CanManageUserAsync(User, account.UserId))
        {
            return NotFound();
        }

        Username = account.Username;
        Input = new InputModel
        {
            HomeDirectory = account.HomeDirectory,
            QuotaMB = account.QuotaMB
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var account = await _db.FtpAccounts.FindAsync(Id);
        if (account == null || !await _scope.CanManageUserAsync(User, account.UserId))
        {
            return NotFound();
        }

        Username = account.Username;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        account.HomeDirectory = Input.HomeDirectory;
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
        await _audit.LogAsync("Update", "FtpAccount", account.Id.ToString(), account.Username);

        // Push changes to vsftpd (simulated on Windows/dev).
        if (!string.IsNullOrWhiteSpace(Input.NewPassword))
        {
            await _ftp.ChangePasswordAsync(account.Username, Input.NewPassword);
        }
        var quotaResult = await _ftp.SetQuotaAsync(account.Username, Input.QuotaMB);
        var suffix = quotaResult.Simulated ? " (vsftpd update simulated)" : " vsftpd updated.";

        TempData["Success"] = $"FTP account '{account.Username}' updated.{suffix}";
        return RedirectToPage("/Ftp/Index");
    }
}
