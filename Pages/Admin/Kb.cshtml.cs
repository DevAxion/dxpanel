using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;

namespace SRXPanel.Pages.Admin;

public class KbModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public KbModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public List<KbArticle> Articles { get; private set; } = new();
    public List<KbCategory> Categories { get; private set; } = new();

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        Articles = await _db.KbArticles.Include(a => a.Category).OrderBy(a => a.CategoryId).ThenBy(a => a.Title).ToListAsync();
        Categories = await _db.KbCategories.OrderBy(c => c.SortOrder).ToListAsync();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var a = await _db.KbArticles.FindAsync(id);
        if (a != null) { _db.KbArticles.Remove(a); await _db.SaveChangesAsync(); }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddCategoryAsync(string name, string icon, string? description, int sortOrder)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            var slug = Slug.From(name);
            if (!await _db.KbCategories.AnyAsync(c => c.Slug == slug))
            {
                _db.KbCategories.Add(new KbCategory { Name = name, Slug = slug, Icon = string.IsNullOrWhiteSpace(icon) ? "bi-journal-text" : icon, Description = description, SortOrder = sortOrder });
                await _db.SaveChangesAsync();
            }
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteCategoryAsync(int id)
    {
        var c = await _db.KbCategories.FindAsync(id);
        if (c != null) { _db.KbCategories.Remove(c); await _db.SaveChangesAsync(); }
        return RedirectToPage();
    }
}
