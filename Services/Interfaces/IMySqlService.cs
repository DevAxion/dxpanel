namespace SRXPanel.Services.Interfaces;

public interface IMySqlService
{
    Task<ServiceResult> CreateDatabaseAsync(string dbName);
    Task<ServiceResult> DeleteDatabaseAsync(string dbName);
    Task<ServiceResult> CreateUserAsync(string username, string password);
    Task<ServiceResult> DeleteUserAsync(string username);
    Task<ServiceResult> GrantPermissionsAsync(string dbName, string username);
    Task<ServiceResult> RevokePermissionsAsync(string dbName, string username);
    Task<double> GetDatabaseSizeAsync(string dbName);
    Task<List<string>> GetAllDatabasesAsync();
}
