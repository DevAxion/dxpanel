namespace SRXPanel.Services.Interfaces;

public interface IFtpService
{
    Task<ServiceResult> CreateFtpUserAsync(string username, string password, string homeDir);
    Task<ServiceResult> DeleteFtpUserAsync(string username);
    Task<ServiceResult> ChangePasswordAsync(string username, string newPassword);
    Task<ServiceResult> SetQuotaAsync(string username, long quotaMB);
}
