namespace SRXPanel.Services.Interfaces;

public interface ISslService
{
    Task<ServiceResult> IssueLetsEncryptAsync(string domain, string email);
    Task<ServiceResult> RenewCertificateAsync(string domain);
    Task<ServiceResult> IssueSelfSignedAsync(string domain);
    Task<DateTime?> GetExpiryDateAsync(string domain);
}
