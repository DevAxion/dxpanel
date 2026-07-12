using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;

namespace SRXPanel.Pages.Admin;

public class KbEditModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public KbEditModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public SelectList CategoryOptions { get; private set; } = null!;
    public bool IsNew => Input.Id == 0;

    public class InputModel
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Slug { get; set; }
        public string Content { get; set; } = string.Empty;
        public int CategoryId { get; set; }
        public string? MetaDescription { get; set; }
        public bool IsPublished { get; set; } = true;
    }

    private async Task LoadOptionsAsync()
    {
        CategoryOptions = new SelectList(await _db.KbCategories.OrderBy(c => c.SortOrder).ToListAsync(), "Id", "Name", Input.CategoryId);
    }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (!await _db.KbCategories.AnyAsync())
        {
            TempData["Error"] = "Create a category first before adding articles.";
            return RedirectToPage("/Admin/Kb");
        }

        if (id.HasValue)
        {
            var a = await _db.KbArticles.FirstOrDefaultAsync(x => x.Id == id);
            if (a == null) return NotFound();
            Input = new InputModel
            {
                Id = a.Id, Title = a.Title, Slug = a.Slug, Content = a.Content,
                CategoryId = a.CategoryId, MetaDescription = a.MetaDescription, IsPublished = a.IsPublished
            };
        }
        await LoadOptionsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Input.Title) || Input.CategoryId == 0)
        {
            ModelState.AddModelError(string.Empty, "Title and category are required.");
            await LoadOptionsAsync();
            return Page();
        }

        var slug = string.IsNullOrWhiteSpace(Input.Slug) ? Slug.From(Input.Title) : Slug.From(Input.Slug);

        KbArticle article;
        if (Input.Id == 0)
        {
            article = new KbArticle { CreatedAt = DateTime.UtcNow };
            _db.KbArticles.Add(article);
        }
        else
        {
            article = await _db.KbArticles.FirstAsync(a => a.Id == Input.Id);
        }

        // Ensure slug uniqueness within the category.
        var baseSlug = slug; var n = 1;
        while (await _db.KbArticles.AnyAsync(a => a.Slug == slug && a.CategoryId == Input.CategoryId && a.Id != article.Id))
            slug = $"{baseSlug}-{++n}";

        article.Title = Input.Title;
        article.Slug = slug;
        article.Content = Input.Content ?? "";
        article.CategoryId = Input.CategoryId;
        article.MetaDescription = Input.MetaDescription;
        article.IsPublished = Input.IsPublished;
        article.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        TempData["Success"] = "Article saved.";
        return RedirectToPage("/Admin/Kb");
    }
}
