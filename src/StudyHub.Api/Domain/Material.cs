namespace StudyHub.Api.Domain;

/// <summary>A set of study notes written by a student.</summary>
public class Material
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;

    /// <summary>The notes themselves (plain text or Markdown).</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Optional course code such as "DCIT 305".</summary>
    public string? CourseCode { get; set; }

    public Guid AuthorId { get; set; }
    public User Author { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<Tag> Tags { get; set; } = new();
    public List<Report> Reports { get; set; } = new();
}
