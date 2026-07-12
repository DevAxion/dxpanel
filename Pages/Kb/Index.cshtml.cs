using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Pages.Kb;

[AllowAnonymous]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public List<KbCategory> Categories { get; private set; } = new();
    public Dictionary<int, int> Counts { get; private set; } = new();
    public List<KbArticle> SearchResults { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Categories = await _db.KbCategories.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync();
        Counts = await _db.KbArticles.Where(a => a.IsPublished)
            .GroupBy(a => a.CategoryId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim();
            SearchResults = await _db.KbArticles
                .Include(a => a.Category)
                .Where(a => a.IsPublished && (a.Title.Contains(term) || a.Content.Contains(term)))
                .OrderByDescending(a => a.Views)
                .Take(20)
                .ToListAsync();
        }
    }
}
