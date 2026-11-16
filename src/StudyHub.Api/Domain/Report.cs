namespace StudyHub.Api.Domain;

public enum ReportStatus
{
    Open = 0,
    Reviewed = 1,
    Dismissed = 2
}

/// <summary>A user flagging a material as inappropriate or copied.</summary>
public class Report
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MaterialId { get; set; }
    public Material Material { get; set; } = null!;

    public Guid ReporterId { get; set; }

    public string Reason { get; set; } = string.Empty;
    public ReportStatus Status { get; set; } = ReportStatus.Open;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
