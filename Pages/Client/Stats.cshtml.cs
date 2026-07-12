using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;

namespace SRXPanel.Pages.Client;

public class StatsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IFileManagerService _fileManager;

    public StatsModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IFileManagerService fileManager)
    {
        _db = db;
        _userManager = userManager;
        _fileManager = fileManager;
    }

    public string LabelsJson { get; set; } = "[]";
    public string BandwidthJson { get; set; } = "[]";
    public string DiskJson { get; set; } = "[]";
    public string RequestLabelsJson { get; set; } = "[]";
    public string RequestsJson { get; set; } = "[]";
    public string ErrorRateJson { get; set; } = "[]";
    public List<(string Page, int Views)> TopPages { get; set; } = new();
    public List<(string Country, int Visits)> Countries { get; set; } = new();
    public long DiskUsedMB { get; set; }

    public async Task OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return;

        DiskUsedMB = _fileManager.GetUsedBytes(user.Id) / 1024 / 1024;
        var domains = await _db.Domains.Where(d => d.UserId == user.Id).Select(d => d.DomainName).ToListAsync();

        // Deterministic pseudo-data keyed by user id so charts are stable across reloads.
        var seed = Math.Abs(user.Id.GetHashCode());
        var rng = new Random(seed);

        var labels = new List<string>();
        var bandwidth = new List<int>();
        var disk = new List<double>();
        for (int i = 29; i >= 0; i--)
        {
            labels.Add(DateTime.UtcNow.AddDays(-i).ToString("MM-dd"));
            bandwidth.Add(rng.Next(200, 2500));
            disk.Add(Math.Round(Math.Max(1, DiskUsedMB) * (0.6 + 0.4 * (29 - i) / 29.0), 1));
        }

        var reqLabels = new List<string>();
        var requests = new List<int>();
        var errorRate = new List<double>();
        for (int i = 23; i >= 0; i--)
        {
            reqLabels.Add($"{DateTime.UtcNow.AddHours(-i):HH}:00");
            requests.Add(rng.Next(50, 800));
            errorRate.Add(Math.Round(rng.NextDouble() * 3, 2));
        }

        LabelsJson = JsonSerializer.Serialize(labels);
        BandwidthJson = JsonSerializer.Serialize(bandwidth);
        DiskJson = JsonSerializer.Serialize(disk);
        RequestLabelsJson = JsonSerializer.Serialize(reqLabels);
        RequestsJson = JsonSerializer.Serialize(requests);
        ErrorRateJson = JsonSerializer.Serialize(errorRate);

        var samplePages = new[] { "/", "/index.php", "/about", "/products", "/contact", "/blog" };
        TopPages = samplePages.Select(p => (p, rng.Next(100, 5000))).OrderByDescending(x => x.Item2).Take(5).ToList();

        var sampleCountries = new[] { "United States", "Germany", "United Kingdom", "Azerbaijan", "Turkey", "France" };
        Countries = sampleCountries.Select(c => (c, rng.Next(50, 3000))).OrderByDescending(x => x.Item2).Take(5).ToList();
    }
}
