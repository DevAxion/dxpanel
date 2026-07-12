using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Pages.Blog;

[AllowAnonymous]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)] public string? Category { get; set; }
    [BindProperty(SupportsGet = true)] public string? Tag { get; set; }

    public List<BlogPost> Posts { get; private set; } = new();
    public List<BlogCategory> Categories { get; private set; } = new();
    public List<BlogTag> Tags { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Categories = await _db.BlogCategories.OrderBy(c => c.Name).ToListAsync();
        Tags = await _db.BlogTags.OrderBy(t => t.Name).ToListAsync();

        var query = _db.BlogPosts
            .Include(p => p.Category)
            .Include(p => p.Tags)
            .Include(p => p.Author)
            .Where(p => p.Status == BlogStatus.Published);

        if (!string.IsNullOrWhiteSpace(Category))
            query = query.Where(p => p.Category != null && p.Category.Slug == Category);

        if (!string.IsNullOrWhiteSpace(Tag))
            query = query.Where(p => p.Tags.Any(t => t.Slug == Tag));

        Posts = await query.OrderByDescending(p => p.PublishedAt).ToListAsync();
    }
}
