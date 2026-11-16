using System.ComponentModel.DataAnnotations;

namespace StudyHub.Api.Contracts;

/// <summary>Used for both creating and updating a material.</summary>
public record MaterialRequest(
    [Required, StringLength(200, MinimumLength = 3)] string Title,
    [Required, StringLength(20000, MinimumLength = 10)] string Body,
    [MaxLength(20)] string? CourseCode,
    [MaxLength(5)] List<string>? Tags);

/// <summary>Query-string options for listing materials. Sort is "newest" (default) or "helpful".</summary>
public record MaterialQuery(
    string? Search = null,
    string? Tag = null,
    string? CourseCode = null,
    int Page = 1,
    int PageSize = 20,
    string? Sort = null);

/// <summary>Lightweight version for lists: the start of the note instead of the whole text.</summary>
public record MaterialSummary(
    Guid Id,
    string Title,
    string? CourseCode,
    List<string> Tags,
    string AuthorName,
    DateTime CreatedAtUtc,
    int HelpfulCount,
    int CommentCount,
    string Preview);

public record MaterialResponse(
    Guid Id,
    string Title,
    string Body,
    string? CourseCode,
    List<string> Tags,
    Guid AuthorId,
    string AuthorName,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int HelpfulCount,
    bool MarkedHelpfulByMe);

/// <summary>Returned after marking or unmarking a note as helpful.</summary>
public record HelpfulResponse(int HelpfulCount, bool MarkedHelpfulByMe);

public record ReportRequest(
    [Required, StringLength(500, MinimumLength = 5)] string Reason);

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
