namespace SRXPanel.Services.Interfaces;

public interface IEmailService
{
    Task<ServiceResult> CreateMailboxAsync(string email, string password, long quotaMB);
    Task<ServiceResult> DeleteMailboxAsync(string email);
    Task<ServiceResult> CreateForwarderAsync(string source, string destination);
    Task<ServiceResult> DeleteForwarderAsync(string source);
    Task<double> GetMailboxSizeAsync(string email);
}
