namespace StudyHub.Api.Domain;

/// <summary>A live quiz game that was played to the end. Saved when the host shows the final results.</summary>
public class GameSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The quiz that was played. Null if the quiz was deleted later; the title below is kept.</summary>
    public Guid? QuizId { get; set; }
    public Quiz? Quiz { get; set; }

    /// <summary>The quiz title at the time it was played, so history still reads well after edits or deletion.</summary>
    public string QuizTitle { get; set; } = string.Empty;

    public Guid HostId { get; set; }
    public bool CorrectOnly { get; set; }

    /// <summary>How many questions were shown before the game ended.</summary>
    public int QuestionsPlayed { get; set; }

    public DateTime PlayedAtUtc { get; set; } = DateTime.UtcNow;

    public List<GameResult> Results { get; set; } = new();
    public List<GameQuestionStat> QuestionStats { get; set; } = new();
}

/// <summary>How one player did in one game.</summary>
public class GameResult
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SessionId { get; set; }
    public GameSession Session { get; set; } = null!;

    /// <summary>The player's account if they were logged in when they joined; null for guests.</summary>
    public Guid? UserId { get; set; }

    public string Nickname { get; set; } = string.Empty;
    public int Score { get; set; }
    public int CorrectCount { get; set; }
    public int Rank { get; set; }
}

/// <summary>How the players did on one question of one game.</summary>
public class GameQuestionStat
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SessionId { get; set; }
    public GameSession Session { get; set; } = null!;

    public int Position { get; set; }
    public string QuestionText { get; set; } = string.Empty;

    /// <summary>Players in the game when the question was shown.</summary>
    public int Asked { get; set; }

    public int Answered { get; set; }
    public int Correct { get; set; }
}
