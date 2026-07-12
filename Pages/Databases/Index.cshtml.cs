using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Pages.Databases;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserScopeService _scope;
    private readonly IAuditLogService _audit;
    private readonly IMySqlService _mysql;

    public IndexModel(ApplicationDbContext db, IUserScopeService scope, IAuditLogService audit, IMySqlService mysql)
    {
        _db = db;
        _scope = scope;
        _audit = audit;
        _mysql = mysql;
    }

    public List<Database> Databases { get; set; } = new();
    public bool ShowOwner { get; set; }
    public bool IsSuperAdmin { get; set; }
    public List<SelectListItem> UserFilterOptions { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? FilterUserId { get; set; }

    public async Task OnGetAsync()
    {
        IsSuperAdmin = User.IsInRole(Roles.SuperAdmin);
        ShowOwner = IsSuperAdmin || User.IsInRole(Roles.Reseller);

        var manageable = await _scope.GetManageableUserIdsAsync(User);

        var query = _db.Databases.Include(d => d.User).Include(d => d.Domain)
            .Where(d => manageable.Contains(d.UserId));

        if (IsSuperAdmin && !string.IsNullOrEmpty(FilterUserId))
        {
            query = query.Where(d => d.UserId == FilterUserId);
        }

        Databases = await query.OrderByDescending(d => d.CreatedAt).ToListAsync();

        if (IsSuperAdmin)
        {
            var users = await _scope.GetManageableUsersAsync(User);
            UserFilterOptions = users
                .OrderBy(u => u.UserName)
                .Select(u => new SelectListItem(u.UserName, u.Id, u.Id == FilterUserId))
                .ToList();
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var database = await _db.Databases.FindAsync(id);
        if (database == null || !await _scope.CanManageUserAsync(User, database.UserId))
        {
            TempData["Error"] = "Database not found or access denied.";
            return RedirectToPage();
        }

        var name = database.DbName;
        var dbUser = database.DbUser;
        _db.Databases.Remove(database);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "Database", id.ToString(), name);

        await _mysql.DeleteDatabaseAsync(name);
        var drop = await _mysql.DeleteUserAsync(dbUser);
        var suffix = drop.Simulated ? " (MySQL drop simulated)" : " MySQL database dropped.";
        TempData["Success"] = $"Database '{name}' has been deleted.{suffix}";
        return RedirectToPage();
    }
}
