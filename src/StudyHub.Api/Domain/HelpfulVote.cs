namespace StudyHub.Api.Domain;

/// <summary>One student marking one note as helpful. A student can do this once per note.</summary>
public class HelpfulVote
{
    public Guid MaterialId { get; set; }
    public Material Material { get; set; } = null!;

    public Guid UserId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
