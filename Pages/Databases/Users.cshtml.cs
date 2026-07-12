using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;

namespace SRXPanel.Pages.Databases;

public class UsersModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserScopeService _scope;
    private readonly ISecretHasher _hasher;
    private readonly IAuditLogService _audit;

    public UsersModel(ApplicationDbContext db, IUserScopeService scope, ISecretHasher hasher, IAuditLogService audit)
    {
        _db = db;
        _scope = scope;
        _hasher = hasher;
        _audit = audit;
    }

    public List<Database> Databases { get; set; } = new();
    public bool ShowOwner { get; set; }

    [BindProperty]
    public ResetInput Reset { get; set; } = new();

    public class ResetInput
    {
        [Required]
        public int DatabaseId { get; set; }

        [Required]
        [StringLength(100, MinimumLength = 8)]
        [DataType(DataType.Password)]
        [Display(Name = "New Password")]
        public string NewPassword { get; set; } = string.Empty;
    }

    public async Task OnGetAsync()
    {
        ShowOwner = User.IsInRole(Roles.SuperAdmin) || User.IsInRole(Roles.Reseller);
        var manageable = await _scope.GetManageableUserIdsAsync(User);
        Databases = await _db.Databases.Include(d => d.User)
            .Where(d => manageable.Contains(d.UserId))
            .OrderBy(d => d.DbUser)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostResetPasswordAsync()
    {
        var database = await _db.Databases.FindAsync(Reset.DatabaseId);
        if (database == null || !await _scope.CanManageUserAsync(User, database.UserId))
        {
            TempData["Error"] = "Database not found or access denied.";
            return RedirectToPage();
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Password must be at least 8 characters.";
            return RedirectToPage();
        }

        database.DbPasswordHash = _hasher.Hash(Reset.NewPassword);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("ResetPassword", "DatabaseUser", database.Id.ToString(), database.DbUser);

        TempData["Success"] = $"Password for DB user '{database.DbUser}' has been reset.";
        return RedirectToPage();
    }
}
