namespace StudyHub.Api.Contracts;

/// <summary>One game in a player's own history.</summary>
public record HistoryItem(
    Guid SessionId,
    string QuizTitle,
    DateTime PlayedAtUtc,
    int Score,
    int CorrectCount,
    int QuestionsPlayed,
    int Rank,
    int Players,
    bool CorrectOnly);

/// <summary>One past game of a quiz, as its writer sees it.</summary>
public record SessionSummary(
    Guid Id,
    DateTime PlayedAtUtc,
    int Players,
    string? Winner,
    int WinnerScore,
    bool CorrectOnly,
    int QuestionsPlayed);

/// <summary>How players have done on one question, across every game of the quiz.</summary>
public record QuestionStatResponse(string Text, int Asked, int Answered, int Correct, int CorrectPercent);

/// <summary>Everything the writer of a quiz can see about how it has been played.</summary>
public record QuizStatsResponse(
    Guid QuizId,
    string Title,
    int TimesPlayed,
    int TotalPlayers,
    int SignedInPlayers,
    int AverageCorrectPercent,
    DateTime? LastPlayedAtUtc,
    IReadOnlyList<SessionSummary> Sessions,
    IReadOnlyList<QuestionStatResponse> Questions);

/// <summary>Site-wide totals for admins. No individual students' results.</summary>
public record SiteStatsResponse(
    int Users,
    int Notes,
    int Comments,
    int Quizzes,
    int GamesPlayed,
    int GamePlayers,
    int GamesLast7Days,
    int PlayersLast7Days,
    int OpenReports);
