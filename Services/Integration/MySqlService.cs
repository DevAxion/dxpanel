using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Integration;

/// <summary>
/// Manages MySQL/MariaDB via the root connection from settings.
/// In simulation mode the SQL is logged (via ICommandRunner) but not executed.
/// </summary>
public class MySqlService : IMySqlService
{
    private const string ServiceName = "mysql";
    private readonly ICommandRunner _runner;
    private readonly PanelSettings _settings;
    private readonly ILogger<MySqlService> _logger;

    public MySqlService(ICommandRunner runner, IOptionsMonitor<PanelSettings> settings, ILogger<MySqlService> logger)
    {
        _runner = runner;
        _settings = settings.CurrentValue;
        _logger = logger;
    }

    public Task<ServiceResult> CreateDatabaseAsync(string dbName) =>
        ExecAsync($"CREATE DATABASE IF NOT EXISTS `{Escape(dbName)}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;",
            $"Database '{dbName}' created.");

    public Task<ServiceResult> DeleteDatabaseAsync(string dbName) =>
        ExecAsync($"DROP DATABASE IF EXISTS `{Escape(dbName)}`;", $"Database '{dbName}' dropped.");

    public Task<ServiceResult> CreateUserAsync(string username, string password) =>
        ExecAsync($"CREATE USER IF NOT EXISTS '{Escape(username)}'@'localhost' IDENTIFIED BY '{Escape(password)}';",
            $"User '{username}' created.");

    public Task<ServiceResult> DeleteUserAsync(string username) =>
        ExecAsync($"DROP USER IF EXISTS '{Escape(username)}'@'localhost';", $"User '{username}' dropped.");

    public Task<ServiceResult> GrantPermissionsAsync(string dbName, string username) =>
        ExecAsync(
            $"GRANT ALL PRIVILEGES ON `{Escape(dbName)}`.* TO '{Escape(username)}'@'localhost'; FLUSH PRIVILEGES;",
            $"Granted all privileges on '{dbName}' to '{username}'.");

    public Task<ServiceResult> RevokePermissionsAsync(string dbName, string username) =>
        ExecAsync(
            $"REVOKE ALL PRIVILEGES ON `{Escape(dbName)}`.* FROM '{Escape(username)}'@'localhost'; FLUSH PRIVILEGES;",
            $"Revoked privileges on '{dbName}' from '{username}'.");

    public async Task<double> GetDatabaseSizeAsync(string dbName)
    {
        if (_runner.SimulationMode) return 0;

        try
        {
            await using var conn = new MySqlConnection(_settings.MySql.BuildConnectionString());
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COALESCE(SUM(data_length + index_length),0)/1024/1024 " +
                "FROM information_schema.tables WHERE table_schema = @db;", conn);
            cmd.Parameters.AddWithValue("@db", dbName);
            var result = await cmd.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? 0 : Math.Round(Convert.ToDouble(result), 2);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetDatabaseSize failed for {Db}", dbName);
            return 0;
        }
    }

    public async Task<List<string>> GetAllDatabasesAsync()
    {
        if (_runner.SimulationMode)
        {
            await _runner.RunAsync("mysql -e 'SHOW DATABASES;'", ServiceName);
            return new List<string>();
        }

        var list = new List<string>();
        try
        {
            await using var conn = new MySqlConnection(_settings.MySql.BuildConnectionString());
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand("SHOW DATABASES;", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(reader.GetString(0));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetAllDatabases failed");
        }
        return list;
    }

    private async Task<ServiceResult> ExecAsync(string sql, string successMessage)
    {
        // Log the SQL through the runner (simulated or as an audit trail).
        var logCmd = await _runner.RunAsync($"mysql -u{_settings.MySql.RootUser} -e \"{sql}\"", ServiceName);

        if (_runner.SimulationMode)
        {
            return ServiceResult.Ok(successMessage, logCmd);
        }

        try
        {
            await using var conn = new MySqlConnection(_settings.MySql.BuildConnectionString());
            await conn.OpenAsync();
            foreach (var statement in sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                await using var cmd = new MySqlCommand(statement, conn);
                await cmd.ExecuteNonQueryAsync();
            }
            return ServiceResult.Ok(successMessage, logCmd);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MySQL exec failed: {Sql}", sql);
            return ServiceResult.Fail($"MySQL error: {ex.Message}", logCmd);
        }
    }

    // Minimal escaping for identifiers/values embedded in admin SQL.
    private static string Escape(string value) => value.Replace("`", "").Replace("'", "").Replace("\\", "");
}
