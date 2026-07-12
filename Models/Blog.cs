using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

public enum BlogStatus
{
    Draft,
    Published
}

public class BlogCategory
{
    public int Id { get; set; }
    [Required, StringLength(80)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(100)] public string Slug { get; set; } = string.Empty;
    [StringLength(300)] public string? Description { get; set; }

    public ICollection<BlogPost> Posts { get; set; } = new List<BlogPost>();
}

public class BlogTag
{
    public int Id { get; set; }
    [Required, StringLength(60)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(80)] public string Slug { get; set; } = string.Empty;

    public ICollection<BlogPost> Posts { get; set; } = new List<BlogPost>();
}

public class BlogPost
{
    public int Id { get; set; }

    [Required, StringLength(200)] public string Title { get; set; } = string.Empty;
    [Required, StringLength(220)] public string Slug { get; set; } = string.Empty;

    /// <summary>Rich HTML body (Quill editor output).</summary>
    public string Content { get; set; } = string.Empty;
    [StringLength(500)] public string? Excerpt { get; set; }

    public string? AuthorId { get; set; }
    public ApplicationUser? Author { get; set; }

    public int? CategoryId { get; set; }
    public BlogCategory? Category { get; set; }

    public ICollection<BlogTag> Tags { get; set; } = new List<BlogTag>();

    public BlogStatus Status { get; set; } = BlogStatus.Draft;

    [StringLength(400)] public string? FeaturedImage { get; set; }

    // SEO
    [StringLength(200)] public string? MetaTitle { get; set; }
    [StringLength(300)] public string? MetaDescription { get; set; }

    public int Views { get; set; }

    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
