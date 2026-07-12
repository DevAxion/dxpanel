using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;
using Database = SRXPanel.Models.Database;

namespace SRXPanel.Services.Developer;

public record TableInfo(string Name, long Rows, long DataBytes, long IndexBytes, string Engine, string Collation)
{
    public long TotalBytes => DataBytes + IndexBytes;
}

public record QueryResult(
    List<string> Columns,
    List<List<string?>> Rows,
    int TotalRows,
    long ElapsedMs,
    int? AffectedRows,
    string? Error,
    bool Simulated);

public record SlowQuerySuggestion(string Query, string Suggestion, long AvgMs);

public enum ExportFormat
{
    Sql,
    Csv,
    Json
}

public enum TableOperation
{
    Optimize,
    Repair,
    Truncate,
    Analyze
}

public interface IDatabaseToolsService
{
    Task<List<Database>> GetDatabasesAsync(string userId);
    Task<Database?> GetDatabaseAsync(string userId, int dbId);

    Task<List<TableInfo>> GetTablesAsync(string userId, int dbId);
    Task<QueryResult> RunQueryAsync(string userId, int dbId, string sql, int page = 1, int pageSize = 50);

    Task<(byte[] content, string fileName, string contentType)> ExportAsync(string userId, int dbId,
        ExportFormat format, string? tableName = null);

    /// <summary>Imports a SQL file, executing it in chunks so a large dump does not exhaust memory.</summary>
    Task<(int statements, string? error)> ImportAsync(string userId, int dbId, Stream sqlFile);

    Task<ServiceResult> TableOperationAsync(string userId, int dbId, string tableName, TableOperation operation);

    Task<List<SlowQuerySuggestion>> GetSlowQuerySuggestionsAsync(string userId, int dbId);
}

public class DatabaseToolsService : IDatabaseToolsService
{
    private const string ServiceName = "mysql";

    /// <summary>Statements a user may run against their own database.</summary>
    private static readonly string[] AllowedStatements =
    {
        "select", "insert", "update", "delete", "show", "describe", "desc", "explain",
        "create", "alter", "drop", "truncate", "replace", "set", "with", "call", "analyze", "optimize", "repair"
    };

    /// <summary>Never allowed: these reach outside the user's own schema.</summary>
    private static readonly string[] ForbiddenStatements =
    {
        "grant", "revoke", "create user", "drop user", "set password", "flush", "shutdown",
        "load data", "into outfile", "into dumpfile", "load_file", "sys_exec"
    };

    private readonly ApplicationDbContext _db;
    private readonly ICommandRunner _runner;
    private readonly PanelSettings _panel;
    private readonly ILogger<DatabaseToolsService> _logger;

    public DatabaseToolsService(ApplicationDbContext db, ICommandRunner runner,
        IOptionsMonitor<PanelSettings> panel, ILogger<DatabaseToolsService> logger)
    {
        _db = db;
        _runner = runner;
        _panel = panel.CurrentValue;
        _logger = logger;
    }

    private bool Simulated => _runner.SimulationMode;

    public Task<List<Database>> GetDatabasesAsync(string userId) =>
        _db.Databases.Where(d => d.UserId == userId).OrderBy(d => d.DbName).ToListAsync();

    public Task<Database?> GetDatabaseAsync(string userId, int dbId) =>
        _db.Databases.FirstOrDefaultAsync(d => d.Id == dbId && d.UserId == userId);

    private async Task<Database> OwnedAsync(string userId, int dbId) =>
        await GetDatabaseAsync(userId, dbId) ?? throw new InvalidOperationException("Database not found.");

    // ---------------- Tables ----------------

    public async Task<List<TableInfo>> GetTablesAsync(string userId, int dbId)
    {
        var database = await OwnedAsync(userId, dbId);
        if (Simulated) return SimulatedTables(database.DbName);

        var tables = new List<TableInfo>();
        await using var connection = new MySqlConnection(_panel.MySql.BuildConnectionString(database.DbName));
        await connection.OpenAsync();

        await using var command = new MySqlCommand(
            @"SELECT TABLE_NAME, TABLE_ROWS, DATA_LENGTH, INDEX_LENGTH, ENGINE, TABLE_COLLATION
              FROM information_schema.TABLES WHERE TABLE_SCHEMA = @schema ORDER BY TABLE_NAME", connection);
        command.Parameters.AddWithValue("@schema", database.DbName);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(new TableInfo(
                reader.GetString(0),
                reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
                reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                reader.IsDBNull(3) ? 0 : reader.GetInt64(3),
                reader.IsDBNull(4) ? "InnoDB" : reader.GetString(4),
                reader.IsDBNull(5) ? "utf8mb4_general_ci" : reader.GetString(5)));
        }
        return tables;
    }

    private static List<TableInfo> SimulatedTables(string dbName)
    {
        var random = new Random(dbName.GetHashCode());
        string[] names = { "users", "sessions", "posts", "comments", "options", "postmeta", "usermeta", "terms", "orders", "order_items" };

        return names.Select(name => new TableInfo(
                name,
                random.Next(12, 48_000),
                random.Next(16_384, 12_000_000),
                random.Next(16_384, 2_000_000),
                "InnoDB",
                "utf8mb4_unicode_ci"))
            .ToList();
    }

    // ---------------- Query ----------------

    public async Task<QueryResult> RunQueryAsync(string userId, int dbId, string sql, int page = 1, int pageSize = 50)
    {
        var database = await OwnedAsync(userId, dbId);

        var (ok, error) = ValidateSql(sql);
        if (!ok) return new QueryResult(new(), new(), 0, 0, null, error, Simulated);

        var isSelect = FirstKeyword(sql) is "select" or "show" or "describe" or "desc" or "explain" or "with";

        if (Simulated)
        {
            await _runner.LogExternalAsync($"mysql {database.DbName}: {Collapse(sql)}", "query executed", true, ServiceName);
            return SimulatedQuery(sql, isSelect, page, pageSize);
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await using var connection = new MySqlConnection(_panel.MySql.BuildConnectionString(database.DbName));
            await connection.OpenAsync();

            // Paginate SELECTs at the server rather than pulling the whole result set.
            var text = isSelect && !sql.Contains(" limit ", StringComparison.OrdinalIgnoreCase)
                ? $"{sql.TrimEnd(';', ' ')} LIMIT {pageSize} OFFSET {(page - 1) * pageSize}"
                : sql;

            await using var command = new MySqlCommand(text, connection) { CommandTimeout = 30 };

            if (!isSelect)
            {
                var affected = await command.ExecuteNonQueryAsync();
                stopwatch.Stop();
                return new QueryResult(new(), new(), 0, stopwatch.ElapsedMilliseconds, affected, null, false);
            }

            await using var reader = await command.ExecuteReaderAsync();

            var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
            var rows = new List<List<string?>>();
            while (await reader.ReadAsync())
            {
                var row = new List<string?>(columns.Count);
                for (var i = 0; i < reader.FieldCount; i++)
                    row.Add(reader.IsDBNull(i) ? null : reader.GetValue(i)?.ToString());
                rows.Add(row);
            }
            stopwatch.Stop();

            return new QueryResult(columns, rows, rows.Count, stopwatch.ElapsedMilliseconds, null, null, false);
        }
        catch (MySqlException ex)
        {
            stopwatch.Stop();
            return new QueryResult(new(), new(), 0, stopwatch.ElapsedMilliseconds, null, ex.Message, false);
        }
    }

    private static QueryResult SimulatedQuery(string sql, bool isSelect, int page, int pageSize)
    {
        if (!isSelect)
            return new QueryResult(new(), new(), 0, Random.Shared.Next(3, 40), Random.Shared.Next(0, 12), null, true);

        var columns = new List<string> { "id", "name", "email", "created_at" };
        var rows = new List<List<string?>>();
        var start = (page - 1) * pageSize + 1;

        for (var i = 0; i < Math.Min(pageSize, 25); i++)
        {
            var id = start + i;
            rows.Add(new List<string?>
            {
                id.ToString(),
                $"Example User {id}",
                $"user{id}@example.com",
                DateTime.UtcNow.AddDays(-id).ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

        return new QueryResult(columns, rows, 25, Random.Shared.Next(1, 18), null, null, true);
    }

    /// <summary>
    /// Rejects statements that would escape the user's own schema. The connection is already
    /// scoped to one database, so this guards against privilege and file-system escapes.
    /// </summary>
    internal static (bool ok, string error) ValidateSql(string sql)
    {
        var trimmed = (sql ?? "").Trim();
        if (trimmed.Length == 0) return (false, "Enter a SQL statement.");
        if (trimmed.Length > 100_000) return (false, "Query is too long.");

        var lowered = Collapse(trimmed).ToLowerInvariant();

        foreach (var forbidden in ForbiddenStatements)
            if (lowered.Contains(forbidden))
                return (false, $"'{forbidden.ToUpperInvariant()}' is not permitted from the panel.");

        var keyword = FirstKeyword(trimmed);
        if (!AllowedStatements.Contains(keyword))
            return (false, $"'{keyword.ToUpperInvariant()}' statements are not permitted here.");

        return (true, "");
    }

    private static string FirstKeyword(string sql)
    {
        var cleaned = Regex.Replace(sql.Trim(), @"^(/\*.*?\*/|--[^\n]*\n|\s)+", "", RegexOptions.Singleline);
        var match = Regex.Match(cleaned, @"^\s*(\w+)");
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : "";
    }

    private static string Collapse(string sql) => Regex.Replace(sql, @"\s+", " ").Trim();

    // ---------------- Export ----------------

    public async Task<(byte[] content, string fileName, string contentType)> ExportAsync(string userId, int dbId,
        ExportFormat format, string? tableName = null)
    {
        var database = await OwnedAsync(userId, dbId);
        var tables = await GetTablesAsync(userId, dbId);

        if (tableName != null)
        {
            if (!tables.Any(t => t.Name == tableName))
                throw new InvalidOperationException("Table not found.");
            tables = tables.Where(t => t.Name == tableName).ToList();
        }

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var baseName = tableName == null ? database.DbName : $"{database.DbName}.{tableName}";

        switch (format)
        {
            case ExportFormat.Sql:
            {
                var sql = await BuildSqlDumpAsync(database, tables, userId, dbId);
                return (Encoding.UTF8.GetBytes(sql), $"{baseName}-{stamp}.sql", "application/sql");
            }
            case ExportFormat.Csv:
            {
                var csv = await BuildCsvAsync(userId, dbId, tables.First().Name);
                return (Encoding.UTF8.GetBytes(csv), $"{baseName}-{stamp}.csv", "text/csv");
            }
            default:
            {
                var json = await BuildJsonAsync(userId, dbId, tables);
                return (Encoding.UTF8.GetBytes(json), $"{baseName}-{stamp}.json", "application/json");
            }
        }
    }

    private async Task<string> BuildSqlDumpAsync(Database database, List<TableInfo> tables, string userId, int dbId)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"-- SRXPanel dump of `{database.DbName}`");
        sb.AppendLine($"-- Generated: {DateTime.UtcNow:u}");
        sb.AppendLine("SET NAMES utf8mb4;");
        sb.AppendLine("SET FOREIGN_KEY_CHECKS = 0;");
        sb.AppendLine();

        foreach (var table in tables)
        {
            sb.AppendLine($"-- Table structure for `{table.Name}` ({table.Rows} rows, {table.Engine})");
            sb.AppendLine($"DROP TABLE IF EXISTS `{table.Name}`;");

            if (Simulated)
            {
                sb.AppendLine($"CREATE TABLE `{table.Name}` (");
                sb.AppendLine("  `id` bigint unsigned NOT NULL AUTO_INCREMENT,");
                sb.AppendLine("  `name` varchar(255) NOT NULL,");
                sb.AppendLine("  `created_at` timestamp NULL DEFAULT NULL,");
                sb.AppendLine("  PRIMARY KEY (`id`)");
                sb.AppendLine($") ENGINE={table.Engine} DEFAULT CHARSET=utf8mb4 COLLATE={table.Collation};");
            }
            else
            {
                var create = await RunQueryAsync(userId, dbId, $"SHOW CREATE TABLE `{table.Name}`");
                if (create.Rows.Count > 0 && create.Rows[0].Count > 1)
                    sb.AppendLine(create.Rows[0][1] + ";");
            }
            sb.AppendLine();
        }

        sb.AppendLine("SET FOREIGN_KEY_CHECKS = 1;");
        if (Simulated)
            sb.AppendLine("\n-- Simulation mode: table structures are illustrative and no rows were dumped.");

        return sb.ToString();
    }

    private async Task<string> BuildCsvAsync(string userId, int dbId, string table)
    {
        var result = await RunQueryAsync(userId, dbId, $"SELECT * FROM `{table}`", 1, 5000);
        var sb = new StringBuilder();

        sb.AppendLine(string.Join(",", result.Columns.Select(Escape)));
        foreach (var row in result.Rows)
            sb.AppendLine(string.Join(",", row.Select(v => Escape(v ?? ""))));

        return sb.ToString();

        static string Escape(string value) =>
            value.Contains(',') || value.Contains('"') || value.Contains('\n')
                ? $"\"{value.Replace("\"", "\"\"")}\""
                : value;
    }

    private async Task<string> BuildJsonAsync(string userId, int dbId, List<TableInfo> tables)
    {
        var export = new Dictionary<string, object>();

        foreach (var table in tables)
        {
            var result = await RunQueryAsync(userId, dbId, $"SELECT * FROM `{table.Name}`", 1, 1000);
            export[table.Name] = result.Rows
                .Select(row => result.Columns
                    .Select((column, index) => new { column, value = row[index] })
                    .ToDictionary(x => x.column, x => x.value))
                .ToList();
        }

        return JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
    }

    // ---------------- Import ----------------

    public async Task<(int statements, string? error)> ImportAsync(string userId, int dbId, Stream sqlFile)
    {
        var database = await OwnedAsync(userId, dbId);

        var executed = 0;
        var batch = new StringBuilder();

        using var reader = new StreamReader(sqlFile);

        MySqlConnection? connection = null;
        if (!Simulated)
        {
            connection = new MySqlConnection(_panel.MySql.BuildConnectionString(database.DbName));
            await connection.OpenAsync();
        }

        try
        {
            // Stream the file line by line and flush on each statement terminator,
            // so a multi-hundred-megabyte dump never lands in memory at once.
            while (await reader.ReadLineAsync() is { } line)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("--") || trimmed.StartsWith("/*!")) continue;

                batch.AppendLine(line);
                if (!trimmed.EndsWith(';')) continue;

                var statement = batch.ToString();
                batch.Clear();

                var (ok, error) = ValidateSql(statement);
                if (!ok) return (executed, $"Statement {executed + 1} rejected: {error}");

                if (connection != null)
                {
                    await using var command = new MySqlCommand(statement, connection) { CommandTimeout = 120 };
                    await command.ExecuteNonQueryAsync();
                }
                executed++;
            }

            if (Simulated)
                await _runner.LogExternalAsync($"mysql {database.DbName} < import.sql", $"{executed} statements", true, ServiceName);

            return (executed, null);
        }
        catch (MySqlException ex)
        {
            _logger.LogError(ex, "SQL import into {Database} failed", database.DbName);
            return (executed, $"Import stopped at statement {executed + 1}: {ex.Message}");
        }
        finally
        {
            if (connection != null) await connection.DisposeAsync();
        }
    }

    // ---------------- Table operations ----------------

    public async Task<ServiceResult> TableOperationAsync(string userId, int dbId, string tableName, TableOperation operation)
    {
        var database = await OwnedAsync(userId, dbId);

        var tables = await GetTablesAsync(userId, dbId);
        if (!tables.Any(t => t.Name == tableName))
            throw new InvalidOperationException("Table not found.");

        // tableName is matched against the real table list above, so it is safe to interpolate.
        var sql = operation switch
        {
            TableOperation.Optimize => $"OPTIMIZE TABLE `{tableName}`",
            TableOperation.Repair => $"REPAIR TABLE `{tableName}`",
            TableOperation.Truncate => $"TRUNCATE TABLE `{tableName}`",
            _ => $"ANALYZE TABLE `{tableName}`"
        };

        if (Simulated)
        {
            var logged = await _runner.LogExternalAsync($"mysql {database.DbName}: {sql}", "OK", true, ServiceName);
            return ServiceResult.Ok($"{operation} completed on {tableName} (simulated).", logged);
        }

        await using var connection = new MySqlConnection(_panel.MySql.BuildConnectionString(database.DbName));
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection) { CommandTimeout = 300 };
        await command.ExecuteNonQueryAsync();

        var result = await _runner.LogExternalAsync($"mysql {database.DbName}: {sql}", "OK", false, ServiceName);
        return ServiceResult.Ok($"{operation} completed on {tableName}.", result);
    }

    // ---------------- Slow queries ----------------

    public async Task<List<SlowQuerySuggestion>> GetSlowQuerySuggestionsAsync(string userId, int dbId)
    {
        var database = await OwnedAsync(userId, dbId);

        // Reading the real performance_schema needs privileges a hosting user does not have,
        // so the panel derives suggestions from table shape instead.
        var tables = await GetTablesAsync(userId, dbId);
        var suggestions = new List<SlowQuerySuggestion>();

        foreach (var table in tables.OrderByDescending(t => t.Rows).Take(4))
        {
            if (table.Rows > 10_000 && table.IndexBytes < table.DataBytes / 8)
                suggestions.Add(new SlowQuerySuggestion(
                    $"SELECT * FROM `{table.Name}` WHERE ...",
                    $"`{table.Name}` holds {table.Rows:N0} rows but only {Bytes(table.IndexBytes)} of indexes. " +
                    "Add an index on the columns you filter and sort by.",
                    120 + table.Rows / 400));

            if (table.DataBytes > 50_000_000)
                suggestions.Add(new SlowQuerySuggestion(
                    $"SELECT COUNT(*) FROM `{table.Name}`",
                    $"`{table.Name}` is {Bytes(table.DataBytes)}. Counting all rows scans the table — cache the count or keep a counter row.",
                    900));
        }

        if (suggestions.Count == 0)
            suggestions.Add(new SlowQuerySuggestion("—",
                $"No obvious indexing problems in `{database.DbName}`. Check the MySQL slow query log for real timings.", 0));

        return suggestions;
    }

    private static string Bytes(long value) => value switch
    {
        > 1_073_741_824 => $"{value / 1073741824.0:0.0} GB",
        > 1_048_576 => $"{value / 1048576.0:0.0} MB",
        > 1024 => $"{value / 1024.0:0.0} KB",
        _ => $"{value} B"
    };
}
