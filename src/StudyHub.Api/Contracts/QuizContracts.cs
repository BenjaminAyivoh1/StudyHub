namespace StudyHub.Api.Contracts;

/// <summary>One question as the website sends and receives it.</summary>
public record QuizQuestionDto(string Text, List<string> Options, int CorrectIndex, int Seconds = 20);

/// <summary>Used for both creating and updating a quiz.</summary>
public record QuizRequest(string Title, List<QuizQuestionDto> Questions, bool IsPublic = false, string? Topic = null);

/// <summary>A quiz in the owner's list.</summary>
public record QuizSummary(
    Guid Id, string Title, int QuestionCount, DateTime CreatedAtUtc, int TimesPlayed, bool IsPublic, string? Topic);

/// <summary>A whole quiz, including the right answers. Only its owner ever receives this.</summary>
public record QuizResponse(
    Guid Id, string Title, List<QuizQuestionDto> Questions, DateTime CreatedAtUtc, bool IsPublic, string? Topic);
