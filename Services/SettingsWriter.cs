using System.Text.Json;
using System.Text.Json.Nodes;

namespace SRXPanel.Services;

/// <summary>
/// Persists the editable "Panel" section (and SimulationMode) back into
/// appsettings.json. IOptionsMonitor picks up the change on the next reload.
/// </summary>
public interface ISettingsWriter
{
    Task SaveAsync(PanelSettings settings, bool simulationMode);
    bool CurrentSimulationMode { get; }

    /// <summary>Persists the "Backup" (off-site S3/Backblaze) section into appsettings.json.</summary>
    Task SaveBackupAsync(BackupSettings settings);
}

public class SettingsWriter : ISettingsWriter
{
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;

    public SettingsWriter(IWebHostEnvironment env, IConfiguration config)
    {
        _env = env;
        _config = config;
    }

    public bool CurrentSimulationMode => _config.GetValue<bool?>("SimulationMode") ?? !OperatingSystem.IsLinux();

    public async Task SaveAsync(PanelSettings settings, bool simulationMode)
    {
        var path = Path.Combine(_env.ContentRootPath, "appsettings.json");

        JsonObject root;
        if (File.Exists(path))
        {
            var existing = await File.ReadAllTextAsync(path);
            root = JsonNode.Parse(existing) as JsonObject ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        root["SimulationMode"] = simulationMode;

        var panelJson = JsonSerializer.SerializeToNode(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        root["Panel"] = panelJson;

        var output = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, output);
    }

    public async Task SaveBackupAsync(BackupSettings settings)
    {
        var path = Path.Combine(_env.ContentRootPath, "appsettings.json");

        JsonObject root;
        if (File.Exists(path))
        {
            var existing = await File.ReadAllTextAsync(path);
            root = JsonNode.Parse(existing) as JsonObject ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        root["Backup"] = JsonSerializer.SerializeToNode(settings, new JsonSerializerOptions { WriteIndented = true });

        var output = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, output);
    }
}
