using System.ComponentModel.DataAnnotations;

namespace StudyHub.Api.Contracts;

public record CommentRequest(
    [Required, StringLength(1000, MinimumLength = 2)] string Body);

public record CommentResponse(
    Guid Id,
    string Body,
    Guid AuthorId,
    string AuthorName,
    DateTime CreatedAtUtc);
