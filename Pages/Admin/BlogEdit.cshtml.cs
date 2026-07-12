using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;

namespace SRXPanel.Pages.Admin;

public class BlogEditModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public BlogEditModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public SelectList CategoryOptions { get; private set; } = null!;
    public bool IsNew => Input.Id == 0;

    public class InputModel
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Slug { get; set; }
        public string? Excerpt { get; set; }
        public string Content { get; set; } = string.Empty;
        public int? CategoryId { get; set; }
        public string? Tags { get; set; }
        public string? FeaturedImage { get; set; }
        public string? MetaTitle { get; set; }
        public string? MetaDescription { get; set; }
        public bool Publish { get; set; }
    }

    private async Task LoadOptionsAsync()
    {
        CategoryOptions = new SelectList(await _db.BlogCategories.OrderBy(c => c.Name).ToListAsync(), "Id", "Name", Input.CategoryId);
    }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id.HasValue)
        {
            var post = await _db.BlogPosts.Include(p => p.Tags).FirstOrDefaultAsync(p => p.Id == id);
            if (post == null) return NotFound();
            Input = new InputModel
            {
                Id = post.Id, Title = post.Title, Slug = post.Slug, Excerpt = post.Excerpt,
                Content = post.Content, CategoryId = post.CategoryId,
                Tags = string.Join(", ", post.Tags.Select(t => t.Name)),
                FeaturedImage = post.FeaturedImage, MetaTitle = post.MetaTitle,
                MetaDescription = post.MetaDescription, Publish = post.Status == BlogStatus.Published
            };
        }
        await LoadOptionsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Input.Title))
        {
            ModelState.AddModelError("Input.Title", "Title is required.");
            await LoadOptionsAsync();
            return Page();
        }

        var slug = string.IsNullOrWhiteSpace(Input.Slug) ? Slug.From(Input.Title) : Slug.From(Input.Slug);

        BlogPost post;
        if (Input.Id == 0)
        {
            post = new BlogPost { CreatedAt = DateTime.UtcNow, AuthorId = _userManager.GetUserId(User) };
            _db.BlogPosts.Add(post);
        }
        else
        {
            post = await _db.BlogPosts.Include(p => p.Tags).FirstAsync(p => p.Id == Input.Id);
        }

        // Ensure slug uniqueness.
        var baseSlug = slug; var n = 1;
        while (await _db.BlogPosts.AnyAsync(p => p.Slug == slug && p.Id != post.Id))
            slug = $"{baseSlug}-{++n}";

        post.Title = Input.Title;
        post.Slug = slug;
        post.Excerpt = Input.Excerpt;
        post.Content = Input.Content ?? "";
        post.CategoryId = Input.CategoryId;
        post.FeaturedImage = Input.FeaturedImage;
        post.MetaTitle = Input.MetaTitle;
        post.MetaDescription = Input.MetaDescription;
        if (Input.Publish && post.Status != BlogStatus.Published) post.PublishedAt = DateTime.UtcNow;
        post.Status = Input.Publish ? BlogStatus.Published : BlogStatus.Draft;

        // Resolve tags (create missing).
        post.Tags.Clear();
        if (!string.IsNullOrWhiteSpace(Input.Tags))
        {
            foreach (var name in Input.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct())
            {
                var tagSlug = Slug.From(name);
                var tag = await _db.BlogTags.FirstOrDefaultAsync(t => t.Slug == tagSlug)
                          ?? new BlogTag { Name = name, Slug = tagSlug };
                post.Tags.Add(tag);
            }
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = "Post saved.";
        return RedirectToPage("/Admin/Blog");
    }
}
