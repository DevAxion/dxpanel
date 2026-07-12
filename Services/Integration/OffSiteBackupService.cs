using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Integration;

public record OffSiteBackupItem(string FileName, long SizeBytes, DateTime LastModified);

/// <summary>
/// Uploads backup archives to an S3-compatible off-site store (AWS S3 or Backblaze
/// B2). In simulation mode — or when no credentials are configured — every call is
/// logged via <see cref="ICommandRunner"/> and returns a faked success so the panel
/// works end-to-end on dev machines. In production it uses the AWS SDK.
/// </summary>
public interface IOffSiteBackupService
{
    /// <summary>Whether off-site backup credentials are configured.</summary>
    bool IsConfigured { get; }

    /// <summary>The configured provider name ("S3" or "Backblaze") for display.</summary>
    string Provider { get; }

    Task<ServiceResult> UploadBackupAsync(string localPath, string fileName);
    Task<List<OffSiteBackupItem>> ListRemoteBackupsAsync();
    Task<ServiceResult> DownloadBackupAsync(string fileName, string localPath);
    Task<ServiceResult> DeleteRemoteBackupAsync(string fileName);
}

public class OffSiteBackupService : IOffSiteBackupService
{
    private const string ServiceName = "backup-offsite";
    private readonly IOptionsMonitor<BackupSettings> _settingsMonitor;
    private readonly ICommandRunner _log;
    private readonly ILogger<OffSiteBackupService> _logger;

    private BackupSettings _settings => _settingsMonitor.CurrentValue;

    public OffSiteBackupService(IOptionsMonitor<BackupSettings> settings, ICommandRunner log, ILogger<OffSiteBackupService> logger)
    {
        _settingsMonitor = settings;
        _log = log;
        _logger = logger;
    }

    public bool IsConfigured => _settings.IsConfigured;
    public string Provider => string.IsNullOrWhiteSpace(_settings.Provider) ? "S3" : _settings.Provider;

    private bool Simulated => _log.SimulationMode || !_settings.IsConfigured;

    private IAmazonS3 CreateClient()
    {
        var config = new AmazonS3Config();
        if (!string.IsNullOrWhiteSpace(_settings.Endpoint))
        {
            // Backblaze B2 / custom S3-compatible endpoints need an explicit URL + path style.
            config.ServiceURL = _settings.Endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? _settings.Endpoint
                : $"https://{_settings.Endpoint}";
            config.ForcePathStyle = true;
        }
        else
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(
                string.IsNullOrWhiteSpace(_settings.Region) ? "us-east-1" : _settings.Region);
        }
        return new AmazonS3Client(_settings.AccessKey, _settings.SecretKey, config);
    }

    public async Task<ServiceResult> UploadBackupAsync(string localPath, string fileName)
    {
        if (Simulated)
        {
            var size = File.Exists(localPath) ? new FileInfo(localPath).Length : 0;
            var cmd = await _log.LogExternalAsync(
                $"{Provider.ToLowerInvariant()}.putObject(bucket={_settings.BucketName}, key={fileName})",
                $"[SIMULATED] Uploaded {fileName} ({size} bytes) to {Provider} off-site storage.",
                simulated: true, ServiceName);
            return ServiceResult.Ok($"Backup '{fileName}' uploaded to off-site storage (simulated).", cmd);
        }

        try
        {
            if (!File.Exists(localPath))
                return ServiceResult.Fail($"Local backup file not found: {localPath}");

            using var client = CreateClient();
            await client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _settings.BucketName,
                Key = fileName,
                FilePath = localPath
            });
            var cmd = await _log.LogExternalAsync(
                $"{Provider.ToLowerInvariant()}.putObject(bucket={_settings.BucketName}, key={fileName})",
                "Upload succeeded.", simulated: false, ServiceName);
            return ServiceResult.Ok($"Backup '{fileName}' uploaded to {Provider}.", cmd);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Off-site upload failed for {File}", fileName);
            return ServiceResult.Fail($"Off-site upload failed: {ex.Message}");
        }
    }

    public async Task<List<OffSiteBackupItem>> ListRemoteBackupsAsync()
    {
        if (Simulated)
        {
            await _log.LogExternalAsync(
                $"{Provider.ToLowerInvariant()}.listObjects(bucket={_settings.BucketName})",
                "[SIMULATED] Listed off-site backups.", simulated: true, ServiceName);
            return new List<OffSiteBackupItem>();
        }

        try
        {
            using var client = CreateClient();
            var response = await client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _settings.BucketName
            });
            var objects = response.S3Objects ?? new List<Amazon.S3.Model.S3Object>();
            return objects
                // Size/LastModified nullability differs across AWS SDK versions; Convert handles both.
                .Select(o => new OffSiteBackupItem(
                    o.Key ?? string.Empty,
                    Convert.ToInt64((object?)o.Size ?? 0L),
                    Convert.ToDateTime((object?)o.LastModified ?? DateTime.MinValue)))
                .OrderByDescending(o => o.LastModified)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Off-site list failed");
            return new List<OffSiteBackupItem>();
        }
    }

    public async Task<ServiceResult> DownloadBackupAsync(string fileName, string localPath)
    {
        if (Simulated)
        {
            var cmd = await _log.LogExternalAsync(
                $"{Provider.ToLowerInvariant()}.getObject(bucket={_settings.BucketName}, key={fileName})",
                $"[SIMULATED] Downloaded {fileName} to {localPath}.", simulated: true, ServiceName);
            return ServiceResult.Ok($"Backup '{fileName}' downloaded (simulated).", cmd);
        }

        try
        {
            using var client = CreateClient();
            using var response = await client.GetObjectAsync(_settings.BucketName, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
            await response.WriteResponseStreamToFileAsync(localPath, false, CancellationToken.None);
            var cmd = await _log.LogExternalAsync(
                $"{Provider.ToLowerInvariant()}.getObject(bucket={_settings.BucketName}, key={fileName})",
                "Download succeeded.", simulated: false, ServiceName);
            return ServiceResult.Ok($"Backup '{fileName}' downloaded from {Provider}.", cmd);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Off-site download failed for {File}", fileName);
            return ServiceResult.Fail($"Off-site download failed: {ex.Message}");
        }
    }

    public async Task<ServiceResult> DeleteRemoteBackupAsync(string fileName)
    {
        if (Simulated)
        {
            var cmd = await _log.LogExternalAsync(
                $"{Provider.ToLowerInvariant()}.deleteObject(bucket={_settings.BucketName}, key={fileName})",
                $"[SIMULATED] Deleted {fileName} from off-site storage.", simulated: true, ServiceName);
            return ServiceResult.Ok($"Backup '{fileName}' deleted from off-site storage (simulated).", cmd);
        }

        try
        {
            using var client = CreateClient();
            await client.DeleteObjectAsync(_settings.BucketName, fileName);
            var cmd = await _log.LogExternalAsync(
                $"{Provider.ToLowerInvariant()}.deleteObject(bucket={_settings.BucketName}, key={fileName})",
                "Delete succeeded.", simulated: false, ServiceName);
            return ServiceResult.Ok($"Backup '{fileName}' deleted from {Provider}.", cmd);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Off-site delete failed for {File}", fileName);
            return ServiceResult.Fail($"Off-site delete failed: {ex.Message}");
        }
    }
}
