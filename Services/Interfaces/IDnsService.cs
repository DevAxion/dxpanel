using SRXPanel.Models;

namespace SRXPanel.Services.Interfaces;

public interface IDnsService
{
    Task<ServiceResult> CreateZoneAsync(string domain);
    Task<ServiceResult> DeleteZoneAsync(string domain);
    Task<ServiceResult> AddRecordAsync(string domain, DnsRecordType type, string name, string value, int ttl, int? priority = null);
    Task<ServiceResult> RemoveRecordAsync(string domain, int recordId);
    Task<ServiceResult> ReloadBindAsync();
    Task<ServiceResult> GetZoneRecordsAsync(string domain);
}
