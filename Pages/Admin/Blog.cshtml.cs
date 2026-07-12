using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;

namespace SRXPanel.Pages.Admin;

public class BlogModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public BlogModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public List<BlogPost> Posts { get; private set; } = new();
    public List<BlogCategory> Categories { get; private set; } = new();

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        Posts = await _db.BlogPosts.Include(p => p.Category).OrderByDescending(p => p.CreatedAt).ToListAsync();
        Categories = await _db.BlogCategories.OrderBy(c => c.Name).ToListAsync();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var p = await _db.BlogPosts.FindAsync(id);
        if (p != null) { _db.BlogPosts.Remove(p); await _db.SaveChangesAsync(); }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var p = await _db.BlogPosts.FindAsync(id);
        if (p != null)
        {
            if (p.Status == BlogStatus.Published) p.Status = BlogStatus.Draft;
            else { p.Status = BlogStatus.Published; p.PublishedAt ??= DateTime.UtcNow; }
            await _db.SaveChangesAsync();
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddCategoryAsync(string name, string? description)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            var slug = Slug.From(name);
            if (!await _db.BlogCategories.AnyAsync(c => c.Slug == slug))
            {
                _db.BlogCategories.Add(new BlogCategory { Name = name, Slug = slug, Description = description });
                await _db.SaveChangesAsync();
            }
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteCategoryAsync(int id)
    {
        var c = await _db.BlogCategories.FindAsync(id);
        if (c != null) { _db.BlogCategories.Remove(c); await _db.SaveChangesAsync(); }
        return RedirectToPage();
    }
}
