using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Pages.Kb;

[AllowAnonymous]
public class ArticleModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public ArticleModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public KbArticle Article { get; private set; } = null!;
    public KbCategory Category { get; private set; } = null!;
    public List<KbArticle> Related { get; private set; } = new();

    private async Task<KbArticle?> LoadAsync(string category, string article)
    {
        return await _db.KbArticles
            .Include(a => a.Category)
            .FirstOrDefaultAsync(a => a.Slug == article && a.Category!.Slug == category && a.IsPublished);
    }

    public async Task<IActionResult> OnGetAsync(string category, string article)
    {
        var found = await LoadAsync(category, article);
        if (found == null) return NotFound();
        Article = found;
        Category = found.Category!;

        await _db.KbArticles.Where(a => a.Id == found.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Views, a => a.Views + 1));

        Related = await _db.KbArticles
            .Where(a => a.CategoryId == found.CategoryId && a.Id != found.Id && a.IsPublished)
            .OrderByDescending(a => a.Views)
            .Take(4)
            .ToListAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostHelpfulAsync(string category, string article, bool helpful)
    {
        var found = await LoadAsync(category, article);
        if (found == null) return NotFound();

        await _db.KbArticles.Where(a => a.Id == found.Id)
            .ExecuteUpdateAsync(s => helpful
                ? s.SetProperty(a => a.HelpfulYes, a => a.HelpfulYes + 1)
                : s.SetProperty(a => a.HelpfulNo, a => a.HelpfulNo + 1));

        TempData["HelpfulThanks"] = helpful
            ? "Thanks for your feedback!"
            : "Thanks — we'll work on improving this article.";
        return RedirectToPage(new { category, article });
    }
}
