using System.ComponentModel.DataAnnotations;

namespace SRXPanel.Models;

public class KbCategory
{
    public int Id { get; set; }
    [Required, StringLength(80)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(100)] public string Slug { get; set; } = string.Empty;
    [StringLength(60)] public string Icon { get; set; } = "bi-journal-text";
    [StringLength(300)] public string? Description { get; set; }
    public int SortOrder { get; set; }

    public ICollection<KbArticle> Articles { get; set; } = new List<KbArticle>();
}

public class KbArticle
{
    public int Id { get; set; }

    [Required, StringLength(200)] public string Title { get; set; } = string.Empty;
    [Required, StringLength(220)] public string Slug { get; set; } = string.Empty;

    /// <summary>Rich HTML body (Quill editor output).</summary>
    public string Content { get; set; } = string.Empty;

    public int CategoryId { get; set; }
    public KbCategory? Category { get; set; }

    public bool IsPublished { get; set; } = true;

    public int Views { get; set; }
    public int HelpfulYes { get; set; }
    public int HelpfulNo { get; set; }

    [StringLength(300)] public string? MetaDescription { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
