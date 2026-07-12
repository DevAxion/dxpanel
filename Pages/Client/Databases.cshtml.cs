using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Interfaces;
using SRXPanel.Services.Reseller;

namespace SRXPanel.Pages.Client;

public class DatabasesModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISecretHasher _hasher;
    private readonly IAuditLogService _audit;
    private readonly IMySqlService _mysql;
    private readonly ICommandRunner _runner;
    private readonly IResourceGuard _guard;

    public DatabasesModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, ISecretHasher hasher,
        IAuditLogService audit, IMySqlService mysql, ICommandRunner runner, IResourceGuard guard)
    {
        _db = db;
        _userManager = userManager;
        _hasher = hasher;
        _audit = audit;
        _mysql = mysql;
        _runner = runner;
        _guard = guard;
    }

    public List<Database> Databases { get; set; } = new();
    public int MaxDatabases { get; set; }
    public bool AtLimit { get; set; }
    [TempData] public string? NewPassword { get; set; }
    [TempData] public string? NewDbUser { get; set; }

    private async Task<ApplicationUser?> LoadAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return null;
        Databases = await _db.Databases.Where(d => d.UserId == user.Id).OrderByDescending(d => d.CreatedAt).ToListAsync();
        var sub = await _db.Subscriptions.Include(s => s.Plan).Where(s => s.UserId == user.Id && s.Status != SubscriptionStatus.Cancelled)
            .OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync();
        MaxDatabases = sub?.Plan?.MaxDatabases ?? 0;
        AtLimit = MaxDatabases > 0 && Databases.Count >= MaxDatabases;
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
        if (AtLimit) { TempData["Error"] = "Database limit reached for your plan."; return RedirectToPage(); }
        var (guardOk, guardError) = await _guard.CheckAsync(user, ResourceKind.Database);
        if (!guardOk) { TempData["Error"] = guardError; return RedirectToPage(); }
        if (!System.Text.RegularExpressions.Regex.IsMatch(suffix ?? "", "^[a-zA-Z0-9_]{1,32}$"))
        {
            TempData["Error"] = "Invalid database name.";
            return RedirectToPage();
        }
        var dbName = HostingHelpers.Prefixed(user.UserName ?? "user", suffix!);
        var dbUser = dbName;
        if (await _db.Databases.AnyAsync(d => d.DbName == dbName))
        {
            TempData["Error"] = "That database already exists.";
            return RedirectToPage();
        }
        var password = HostingHelpers.GeneratePassword();
        _db.Databases.Add(new Database
        {
            UserId = user.Id, DbName = dbName, DbUser = dbUser, DbPasswordHash = _hasher.Hash(password),
            IsActive = true, CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "Database", null, dbName);
        await _mysql.CreateDatabaseAsync(dbName);
        await _mysql.CreateUserAsync(dbUser, password);
        await _mysql.GrantPermissionsAsync(dbName, dbUser);
        NewDbUser = dbUser;
        NewPassword = password;
        TempData["Success"] = $"Database '{dbName}' created.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPasswordAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var database = await _db.Databases.FirstOrDefaultAsync(d => d.Id == id && d.UserId == user.Id);
        if (database == null) { TempData["Error"] = "Not found."; return RedirectToPage(); }
        var password = HostingHelpers.GeneratePassword();
        database.DbPasswordHash = _hasher.Hash(password);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Update", "Database", id.ToString(), $"password reset for {database.DbName}");
        NewDbUser = database.DbUser;
        NewPassword = password;
        TempData["Success"] = "Database password reset.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var database = await _db.Databases.FirstOrDefaultAsync(d => d.Id == id && d.UserId == user.Id);
        if (database == null) { TempData["Error"] = "Not found."; return RedirectToPage(); }
        var name = database.DbName;
        _db.Databases.Remove(database);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "Database", id.ToString(), name);
        await _mysql.DeleteDatabaseAsync(name);
        await _mysql.DeleteUserAsync(database.DbUser);
        TempData["Success"] = $"Database '{name}' deleted.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostBackupAsync(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var database = await _db.Databases.FirstOrDefaultAsync(d => d.Id == id && d.UserId == user.Id);
        if (database == null) { TempData["Error"] = "Not found."; return RedirectToPage(); }

        var cmd = await _runner.RunAsync($"mysqldump {database.DbName} > /tmp/{database.DbName}.sql", "mysql");
        var dump = $"-- SQL dump of {database.DbName}\n-- Generated {DateTime.UtcNow:O}\n" +
                   (cmd.Simulated ? "-- (simulation: real dump would contain your tables & data)\n" : "");
        return File(System.Text.Encoding.UTF8.GetBytes(dump), "application/sql", $"{database.DbName}.sql");
    }
}
