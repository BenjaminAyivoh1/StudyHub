namespace StudyHub.Api.Domain;

/// <summary>A question or correction a student leaves under a note.</summary>
public class Comment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MaterialId { get; set; }
    public Material Material { get; set; } = null!;

    public Guid AuthorId { get; set; }
    public User Author { get; set; } = null!;

    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
