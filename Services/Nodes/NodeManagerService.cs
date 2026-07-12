using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Services.Nodes;

public record NodeCapacity(int NodeId, string Name, double CpuPercent, double RamPercent, double DiskPercent,
    int DomainCount, int UserCount, int Weight, bool Accepting)
{
    /// <summary>Lower score = more capacity. Weight lets an operator prefer certain nodes.</summary>
    public double Score => (CpuPercent * 0.4 + RamPercent * 0.3 + DiskPercent * 0.3) / Math.Max(1, Weight) * 100;
}

public record RebalanceSuggestion(int DomainId, string DomainName, int FromNodeId, string FromNode,
    int ToNodeId, string ToNode, string Reason);

public enum MigrationType
{
    Full,
    FilesOnly,
    DatabaseOnly
}

public record MigrationPreflight(long SourceDiskGB, long TargetFreeGB, long DatabaseSizeMB,
    int EstimatedSeconds, bool TargetHasServices, List<string> Warnings)
{
    public bool CanProceed => Warnings.All(w => !w.StartsWith("BLOCK"));
}

public interface INodeManagerService
{
    Task<List<ServerNode>> GetAllNodesAsync();
    Task<ServerNode?> GetNodeAsync(int nodeId);

    Task<ServerMetric?> GetLatestMetricAsync(int nodeId);
    Task<List<ServerMetric>> GetMetricHistoryAsync(int nodeId, int hours = 1);

    Task AssignUserToNodeAsync(string userId, int nodeId);
    Task AssignDomainToNodeAsync(int domainId, int nodeId, bool primary = true);

    Task<NodeCapacity?> GetNodeCapacityAsync(int nodeId);
    Task<List<NodeCapacity>> GetFleetCapacityAsync();

    /// <summary>The least-loaded node accepting new placement (respects weight, threshold, geo hint).</summary>
    Task<ServerNode?> GetBestNodeAsync(string? countryHint = null);

    Task<List<RebalanceSuggestion>> SuggestRebalanceAsync();

    Task<int> DomainCountAsync(int nodeId);
    Task<int> UserCountAsync(int nodeId);

    Task<MigrationPreflight> PreflightAsync(int domainId, int fromNodeId, int toNodeId, MigrationType type);

    /// <summary>Kicks off a background migration; returns a migration id for the progress stream.</summary>
    int StartMigration(int domainId, int fromNodeId, int toNodeId, MigrationType type, string triggeredBy);
}

public class NodeManagerService : INodeManagerService
{
    private readonly ApplicationDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private static int _migrationCounter = 1000;

    public NodeManagerService(ApplicationDbContext db, IServiceScopeFactory scopeFactory)
    {
        _db = db;
        _scopeFactory = scopeFactory;
    }

    public Task<List<ServerNode>> GetAllNodesAsync() =>
        _db.ServerNodes.Include(n => n.Services).OrderBy(n => n.Name).ToListAsync();

    public Task<ServerNode?> GetNodeAsync(int nodeId) =>
        _db.ServerNodes.Include(n => n.Services).FirstOrDefaultAsync(n => n.Id == nodeId);

    public Task<ServerMetric?> GetLatestMetricAsync(int nodeId) =>
        _db.ServerMetrics.Where(m => m.NodeId == nodeId).OrderByDescending(m => m.Timestamp).FirstOrDefaultAsync();

    public Task<List<ServerMetric>> GetMetricHistoryAsync(int nodeId, int hours = 1)
    {
        var cutoff = DateTime.UtcNow.AddHours(-hours);
        return _db.ServerMetrics.Where(m => m.NodeId == nodeId && m.Timestamp >= cutoff)
            .OrderBy(m => m.Timestamp).ToListAsync();
    }

    public async Task AssignUserToNodeAsync(string userId, int nodeId)
    {
        var existing = await _db.UserNodes.FirstOrDefaultAsync(x => x.UserId == userId);
        if (existing != null) existing.NodeId = nodeId;
        else _db.UserNodes.Add(new UserNode { UserId = userId, NodeId = nodeId, AssignedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
    }

    public async Task AssignDomainToNodeAsync(int domainId, int nodeId, bool primary = true)
    {
        var existing = await _db.DomainNodes.FirstOrDefaultAsync(x => x.DomainId == domainId);
        if (existing != null) { existing.NodeId = nodeId; existing.IsPrimary = primary; }
        else _db.DomainNodes.Add(new DomainNode { DomainId = domainId, NodeId = nodeId, IsPrimary = primary, AssignedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
    }

    public Task<int> DomainCountAsync(int nodeId) => _db.DomainNodes.CountAsync(x => x.NodeId == nodeId);
    public Task<int> UserCountAsync(int nodeId) => _db.UserNodes.CountAsync(x => x.NodeId == nodeId);

    public async Task<NodeCapacity?> GetNodeCapacityAsync(int nodeId)
    {
        var node = await _db.ServerNodes.FirstOrDefaultAsync(n => n.Id == nodeId);
        if (node == null) return null;

        var metric = await GetLatestMetricAsync(nodeId);
        return new NodeCapacity(node.Id, node.Name,
            metric?.CpuPercent ?? 0, metric?.RamPercent ?? 0, metric?.DiskPercent ?? 0,
            await DomainCountAsync(nodeId), await UserCountAsync(nodeId), node.Weight,
            node.IsActive && node.Status == NodeStatus.Online && node.Weight > 0);
    }

    public async Task<List<NodeCapacity>> GetFleetCapacityAsync()
    {
        var nodes = await _db.ServerNodes.Where(n => n.IsActive).ToListAsync();
        var result = new List<NodeCapacity>();
        foreach (var node in nodes)
        {
            var capacity = await GetNodeCapacityAsync(node.Id);
            if (capacity != null) result.Add(capacity);
        }
        return result;
    }

    public async Task<ServerNode?> GetBestNodeAsync(string? countryHint = null)
    {
        var capacities = await GetFleetCapacityAsync();
        var accepting = capacities.Where(c => c.Accepting).ToList();
        if (!accepting.Any()) return null;

        // Geographic hint: prefer a node whose location mentions the country/region.
        if (!string.IsNullOrWhiteSpace(countryHint))
        {
            var nodes = await _db.ServerNodes.Where(n => n.IsActive).ToListAsync();
            var geoMatch = accepting
                .Select(c => (cap: c, node: nodes.First(n => n.Id == c.NodeId)))
                .Where(x => x.node.Location.Contains(countryHint, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.cap.Score)
                .FirstOrDefault();
            if (geoMatch.node != null) return geoMatch.node;
        }

        var best = accepting.OrderBy(c => c.Score).First();
        return await _db.ServerNodes.FirstOrDefaultAsync(n => n.Id == best.NodeId);
    }

    public async Task<List<RebalanceSuggestion>> SuggestRebalanceAsync()
    {
        var capacities = await GetFleetCapacityAsync();
        if (capacities.Count < 2) return new List<RebalanceSuggestion>();

        var overloaded = capacities.Where(c => c.CpuPercent > 80 || c.RamPercent > 85).OrderByDescending(c => c.Score).ToList();
        var suggestions = new List<RebalanceSuggestion>();

        foreach (var hot in overloaded)
        {
            var target = capacities.Where(c => c.NodeId != hot.NodeId && c.Accepting).OrderBy(c => c.Score).FirstOrDefault();
            if (target == null || target.Score >= hot.Score) continue;

            // Move the most recently assigned domain off the hot node.
            var domain = await _db.DomainNodes.Include(d => d.Domain)
                .Where(d => d.NodeId == hot.NodeId)
                .OrderByDescending(d => d.AssignedAt)
                .FirstOrDefaultAsync();
            if (domain?.Domain == null) continue;

            suggestions.Add(new RebalanceSuggestion(domain.DomainId, domain.Domain.DomainName,
                hot.NodeId, hot.Name, target.NodeId, target.Name,
                $"{hot.Name} is at {hot.CpuPercent:0}% CPU / {hot.RamPercent:0}% RAM"));
        }

        return suggestions;
    }

    public async Task<MigrationPreflight> PreflightAsync(int domainId, int fromNodeId, int toNodeId, MigrationType type)
    {
        var warnings = new List<string>();

        var target = await _db.ServerNodes.Include(n => n.Services).FirstOrDefaultAsync(n => n.Id == toNodeId);
        var targetMetric = await GetLatestMetricAsync(toNodeId);

        var sourceDisk = new Random(domainId).Next(2, 25);
        var targetFree = target != null ? (long)(target.DiskGB * (1 - (targetMetric?.DiskPercent ?? 40) / 100.0)) : 0;
        var dbSize = new Random(domainId + 7).Next(20, 800);

        if (target == null) warnings.Add("BLOCK: target node not found.");
        else
        {
            if (target.Status != NodeStatus.Online) warnings.Add("BLOCK: target node is not online.");
            if (targetFree < sourceDisk) warnings.Add($"BLOCK: target has only {targetFree} GB free, needs {sourceDisk} GB.");

            var hasNginx = target.Services.Any(s => s.ServiceType == ServerServiceType.Nginx && s.Status == ServerServiceStatus.Running);
            var hasMysql = target.Services.Any(s => s.ServiceType == ServerServiceType.MySQL && s.Status == ServerServiceStatus.Running);
            if (type != MigrationType.DatabaseOnly && !hasNginx) warnings.Add("Nginx is not running on the target — vhost will be created but sites won't serve until it starts.");
            if (type != MigrationType.FilesOnly && !hasMysql) warnings.Add("MySQL is not running on the target — the database import may fail.");
        }

        var estimated = type switch
        {
            MigrationType.FilesOnly => sourceDisk * 3,
            MigrationType.DatabaseOnly => dbSize / 10,
            _ => sourceDisk * 3 + dbSize / 10
        } + 20;

        var targetHasServices = target?.Services.Any(s => s.Status == ServerServiceStatus.Running) ?? false;
        return new MigrationPreflight(sourceDisk, targetFree, dbSize, estimated, targetHasServices, warnings);
    }

    public int StartMigration(int domainId, int fromNodeId, int toNodeId, MigrationType type, string triggeredBy)
    {
        var migrationId = System.Threading.Interlocked.Increment(ref _migrationCounter);

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var migration = scope.ServiceProvider.GetRequiredService<INodeMigrationService>();
            await migration.RunAsync(migrationId, domainId, fromNodeId, toNodeId, type, triggeredBy);
        });

        return migrationId;
    }
}
