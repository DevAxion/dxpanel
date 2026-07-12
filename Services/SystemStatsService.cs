using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SRXPanel.Services;

public class SystemStats
{
    public double CpuUsagePercent { get; set; }
    public double MemoryUsagePercent { get; set; }
    public long MemoryTotalMB { get; set; }
    public long MemoryUsedMB { get; set; }
    public double DiskUsagePercent { get; set; }
    public long DiskTotalMB { get; set; }
    public long DiskUsedMB { get; set; }
    public string OsDescription { get; set; } = string.Empty;
}

public interface ISystemStatsService
{
    Task<SystemStats> GetStatsAsync();
}

public class SystemStatsService : ISystemStatsService
{
    public async Task<SystemStats> GetStatsAsync()
    {
        var stats = new SystemStats
        {
            OsDescription = RuntimeInformation.OSDescription
        };

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                await PopulateLinuxStatsAsync(stats);
            }
            else
            {
                PopulateCrossPlatformFallback(stats);
            }
        }
        catch
        {
            PopulateCrossPlatformFallback(stats);
        }

        return stats;
    }

    private async Task PopulateLinuxStatsAsync(SystemStats stats)
    {
        // CPU usage via /proc/stat sampled twice
        var (idle1, total1) = ReadCpuTimes();
        await Task.Delay(200);
        var (idle2, total2) = ReadCpuTimes();
        var idleDelta = idle2 - idle1;
        var totalDelta = total2 - total1;
        stats.CpuUsagePercent = totalDelta > 0
            ? Math.Round(100.0 * (1.0 - (double)idleDelta / totalDelta), 1)
            : 0;

        // Memory via /proc/meminfo
        if (File.Exists("/proc/meminfo"))
        {
            var lines = await File.ReadAllLinesAsync("/proc/meminfo");
            long totalKb = 0, availableKb = 0;
            foreach (var line in lines)
            {
                if (line.StartsWith("MemTotal:"))
                    totalKb = ParseMemInfoValue(line);
                else if (line.StartsWith("MemAvailable:"))
                    availableKb = ParseMemInfoValue(line);
            }

            stats.MemoryTotalMB = totalKb / 1024;
            var usedKb = totalKb - availableKb;
            stats.MemoryUsedMB = usedKb / 1024;
            stats.MemoryUsagePercent = totalKb > 0 ? Math.Round(100.0 * usedKb / totalKb, 1) : 0;
        }

        // Disk usage via `df -k /`
        var psi = new ProcessStartInfo
        {
            FileName = "df",
            Arguments = "-k /",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process != null)
        {
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            var line = output.Split('\n').Skip(1).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
            if (line != null)
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5)
                {
                    var totalKb = long.Parse(parts[1]);
                    var usedKb = long.Parse(parts[2]);
                    stats.DiskTotalMB = totalKb / 1024;
                    stats.DiskUsedMB = usedKb / 1024;
                    stats.DiskUsagePercent = totalKb > 0 ? Math.Round(100.0 * usedKb / totalKb, 1) : 0;
                }
            }
        }
    }

    private static long ParseMemInfoValue(string line)
    {
        var parts = line.Split(':', StringSplitOptions.RemoveEmptyEntries);
        var valuePart = parts[1].Trim().Split(' ')[0];
        return long.TryParse(valuePart, out var value) ? value : 0;
    }

    private static (long idle, long total) ReadCpuTimes()
    {
        var line = File.ReadLines("/proc/stat").First(l => l.StartsWith("cpu "));
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Select(long.Parse).ToArray();
        var idle = parts[3];
        var total = parts.Sum();
        return (idle, total);
    }

    private void PopulateCrossPlatformFallback(SystemStats stats)
    {
        // Fallback for non-Linux dev environments (e.g. Windows dev machine)
        try
        {
            var drive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.DriveType == DriveType.Fixed);
            if (drive != null)
            {
                stats.DiskTotalMB = drive.TotalSize / 1024 / 1024;
                stats.DiskUsedMB = (drive.TotalSize - drive.AvailableFreeSpace) / 1024 / 1024;
                stats.DiskUsagePercent = drive.TotalSize > 0
                    ? Math.Round(100.0 * (drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize, 1)
                    : 0;
            }
        }
        catch
        {
            // ignore
        }

        stats.CpuUsagePercent = 0;
        stats.MemoryUsagePercent = 0;
    }
}
