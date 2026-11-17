namespace StudyHub.Api.Contracts;

/// <summary>A quiz its writer has shared for practice. The right answers are never included.</summary>
public record PublicQuizSummary(Guid Id, string Title, string? Topic, int QuestionCount, string AuthorName);

/// <summary>
/// Starts a practice run. Give a QuizId to practise one quiz, or leave it out for a random mix
/// of shared questions, optionally from one topic.
/// </summary>
public record PracticeStartRequest(Guid? QuizId = null, string? Topic = null, int? Count = null);

/// <summary>A question as the student sees it during practice: no right answer.</summary>
public record PracticeQuestionView(int Number, string Text, IReadOnlyList<string> Options);

public record PracticeStartResponse(Guid PracticeId, string Title, IReadOnlyList<PracticeQuestionView> Questions);

public record PracticeAnswerRequest(int Question, int Option);

/// <summary>Whether the answer was right, which answer was right, and the running score.</summary>
public record PracticeAnswerResponse(bool Correct, int CorrectIndex, int Score, int Answered, int Total);

public record PracticeFinishResponse(int Score, int Total, bool Saved);
