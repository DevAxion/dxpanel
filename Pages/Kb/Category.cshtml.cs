using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Pages.Kb;

[AllowAnonymous]
public class CategoryModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public CategoryModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public KbCategory Category { get; private set; } = null!;
    public List<KbArticle> Articles { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(string category)
    {
        var cat = await _db.KbCategories.FirstOrDefaultAsync(c => c.Slug == category);
        if (cat == null) return NotFound();
        Category = cat;

        Articles = await _db.KbArticles
            .Where(a => a.CategoryId == cat.Id && a.IsPublished)
            .OrderByDescending(a => a.Views)
            .ToListAsync();

        return Page();
    }
}
