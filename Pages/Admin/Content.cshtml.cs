using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Pages.Admin;

public class ContentModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public ContentModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public List<FeatureItem> Features { get; private set; } = new();
    public List<StatCounter> Stats { get; private set; } = new();
    public List<Testimonial> Testimonials { get; private set; } = new();

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        Features = await _db.FeatureItems.OrderBy(f => f.SortOrder).ToListAsync();
        Stats = await _db.StatCounters.OrderBy(s => s.SortOrder).ToListAsync();
        Testimonials = await _db.Testimonials.OrderBy(t => t.SortOrder).ToListAsync();
    }

    // ---- Features ----
    public async Task<IActionResult> OnPostAddFeatureAsync(string icon, string title, string description, int sortOrder)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            _db.FeatureItems.Add(new FeatureItem { Icon = string.IsNullOrWhiteSpace(icon) ? "bi-stars" : icon, Title = title, Description = description ?? "", SortOrder = sortOrder });
            await _db.SaveChangesAsync();
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteFeatureAsync(int id)
    {
        var f = await _db.FeatureItems.FindAsync(id);
        if (f != null) { _db.FeatureItems.Remove(f); await _db.SaveChangesAsync(); }
        return RedirectToPage();
    }

    // ---- Stats ----
    public async Task<IActionResult> OnPostAddStatAsync(string label, long value, string? suffix, string icon, int sortOrder)
    {
        if (!string.IsNullOrWhiteSpace(label))
        {
            _db.StatCounters.Add(new StatCounter { Label = label, Value = value, Suffix = suffix, Icon = string.IsNullOrWhiteSpace(icon) ? "bi-graph-up-arrow" : icon, SortOrder = sortOrder });
            await _db.SaveChangesAsync();
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteStatAsync(int id)
    {
        var s = await _db.StatCounters.FindAsync(id);
        if (s != null) { _db.StatCounters.Remove(s); await _db.SaveChangesAsync(); }
        return RedirectToPage();
    }

    // ---- Testimonials ----
    public async Task<IActionResult> OnPostAddTestimonialAsync(string name, string? company, string content, int rating, string? photoUrl, int sortOrder)
    {
        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(content))
        {
            _db.Testimonials.Add(new Testimonial
            {
                Name = name, Company = company, Content = content,
                Rating = Math.Clamp(rating, 1, 5), PhotoUrl = photoUrl, SortOrder = sortOrder, IsPublished = true
            });
            await _db.SaveChangesAsync();
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteTestimonialAsync(int id)
    {
        var t = await _db.Testimonials.FindAsync(id);
        if (t != null) { _db.Testimonials.Remove(t); await _db.SaveChangesAsync(); }
        return RedirectToPage();
    }
}
