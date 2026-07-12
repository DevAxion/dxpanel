using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Developer;
using Db = SRXPanel.Models.Database;

namespace SRXPanel.Pages.Client.Developer;

public class DatabaseModel : PageModel
{
    private readonly IDatabaseToolsService _tools;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _auditLog;

    public DatabaseModel(IDatabaseToolsService tools, UserManager<ApplicationUser> userManager, IAuditLogService auditLog)
    {
        _tools = tools;
        _userManager = userManager;
        _auditLog = auditLog;
    }

    [BindProperty(SupportsGet = true)] public int? DbId { get; set; }
    [BindProperty(SupportsGet = true)] public int Page_ { get; set; } = 1;

    public List<Db> Databases { get; private set; } = new();
    public Db? Selected { get; private set; }
    public List<TableInfo> Tables { get; private set; } = new();
    public List<SlowQuerySuggestion> Suggestions { get; private set; } = new();

    public QueryResult? QueryResult { get; private set; }
    public string? Sql { get; private set; }

    public long TotalBytes => Tables.Sum(t => t.TotalBytes);
    public long TotalRows => Tables.Sum(t => t.Rows);

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        Databases = await _tools.GetDatabasesAsync(user.Id);
        if (!Databases.Any()) return Page();

        DbId ??= Databases.First().Id;
        Selected = await _tools.GetDatabaseAsync(user.Id, DbId.Value);
        if (Selected == null) return NotFound();

        Tables = await _tools.GetTablesAsync(user.Id, Selected.Id);
        Suggestions = await _tools.GetSlowQuerySuggestionsAsync(user.Id, Selected.Id);

        return Page();
    }

    public async Task<IActionResult> OnPostQueryAsync(string sql, int page = 1)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        Databases = await _tools.GetDatabasesAsync(user.Id);
        Selected = await _tools.GetDatabaseAsync(user.Id, DbId ?? 0);
        if (Selected == null) return NotFound();

        Sql = sql;
        Page_ = Math.Max(1, page);
        QueryResult = await _tools.RunQueryAsync(user.Id, Selected.Id, sql, Page_, 50);

        Tables = await _tools.GetTablesAsync(user.Id, Selected.Id);
        Suggestions = await _tools.GetSlowQuerySuggestionsAsync(user.Id, Selected.Id);

        if (QueryResult.Error == null)
            await _auditLog.LogAsync("Query", "Database", Selected.Id.ToString(), Truncate(sql, 200));

        return Page();
    }

    public async Task<IActionResult> OnPostExportAsync(int dbId, ExportFormat format, string? tableName)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        try
        {
            var (content, fileName, contentType) = await _tools.ExportAsync(user.Id, dbId, format, tableName);
            await _auditLog.LogAsync("Export", "Database", dbId.ToString(), format.ToString());
            return File(content, contentType, fileName);
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToPage(new { dbId });
        }
    }

    public async Task<IActionResult> OnPostImportAsync(int dbId, IFormFile sqlFile)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (sqlFile == null || sqlFile.Length == 0)
        {
            TempData["Error"] = "Choose a .sql file to import.";
            return RedirectToPage(new { dbId });
        }

        if (!sqlFile.FileName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Only .sql files can be imported.";
            return RedirectToPage(new { dbId });
        }

        await using var stream = sqlFile.OpenReadStream();
        var (statements, error) = await _tools.ImportAsync(user.Id, dbId, stream);

        if (error != null) TempData["Error"] = error;
        else TempData["Success"] = $"Imported {statements} statements from {sqlFile.FileName}.";

        await _auditLog.LogAsync("Import", "Database", dbId.ToString(), sqlFile.FileName);
        return RedirectToPage(new { dbId });
    }

    public async Task<IActionResult> OnPostTableOpAsync(int dbId, string tableName, TableOperation operation)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        try
        {
            var result = await _tools.TableOperationAsync(user.Id, dbId, tableName, operation);
            await _auditLog.LogAsync(operation.ToString(), "DatabaseTable", dbId.ToString(), tableName);
            TempData["Success"] = result.Message;
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage(new { dbId });
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}
