using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Pages.Blog;

[AllowAnonymous]
public class PostModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public PostModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public BlogPost Post { get; private set; } = null!;
    public List<BlogPost> Related { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        var post = await _db.BlogPosts
            .Include(p => p.Category)
            .Include(p => p.Tags)
            .Include(p => p.Author)
            .FirstOrDefaultAsync(p => p.Slug == slug && p.Status == BlogStatus.Published);

        if (post == null) return NotFound();
        Post = post;

        // Increment views without tracking the whole graph.
        await _db.BlogPosts.Where(p => p.Id == post.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Views, p => p.Views + 1));

        Related = await _db.BlogPosts
            .Where(p => p.Status == BlogStatus.Published && p.Id != post.Id && p.CategoryId == post.CategoryId)
            .OrderByDescending(p => p.PublishedAt)
            .Take(3)
            .ToListAsync();

        return Page();
    }
}
