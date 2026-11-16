namespace StudyHub.Api.Contracts;

/// <summary>Tells the website who is logged in and whether to show the admin pages.</summary>
public record MeResponse(Guid UserId, bool IsAdmin);

/// <summary>One open report, with enough about the note to decide what to do.</summary>
public record AdminReportResponse(
    Guid Id,
    string Reason,
    DateTime CreatedAtUtc,
    string ReporterName,
    Guid MaterialId,
    string MaterialTitle,
    string MaterialAuthorName,
    string MaterialPreview);
