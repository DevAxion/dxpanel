namespace SRXPanel.Services.Interfaces;

public interface INginxService
{
    Task<ServiceResult> CreateVirtualHostAsync(string domain, string documentRoot, string phpVersion);
    Task<ServiceResult> DeleteVirtualHostAsync(string domain);
    Task<ServiceResult> EnableSslAsync(string domain, string certPath, string keyPath);
    Task<ServiceResult> DisableSslAsync(string domain);
    Task<ServiceResult> GetStatusAsync();
    Task<ServiceResult> ReloadNginxAsync();
}
