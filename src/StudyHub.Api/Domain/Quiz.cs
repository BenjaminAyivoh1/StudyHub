namespace StudyHub.Api.Domain;

/// <summary>A set of multiple-choice questions that its owner can run as a live game.</summary>
public class Quiz
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;

    public Guid OwnerId { get; set; }
    public User Owner { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<QuizQuestion> Questions { get; set; } = new();
}

/// <summary>One question with two to four possible answers, exactly one of them correct.</summary>
public class QuizQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid QuizId { get; set; }
    public Quiz Quiz { get; set; } = null!;

    /// <summary>Order within the quiz, starting at 0.</summary>
    public int Position { get; set; }

    public string Text { get; set; } = string.Empty;

    public string OptionA { get; set; } = string.Empty;
    public string OptionB { get; set; } = string.Empty;
    public string? OptionC { get; set; }
    public string? OptionD { get; set; }

    /// <summary>Which answer is right: 0 for A, 1 for B, and so on.</summary>
    public int CorrectIndex { get; set; }

    /// <summary>How long players get to answer.</summary>
    public int Seconds { get; set; } = 20;

    /// <summary>The answers that are filled in, in order.</summary>
    public List<string> GetOptions()
    {
        var options = new List<string> { OptionA, OptionB };
        if (!string.IsNullOrEmpty(OptionC)) options.Add(OptionC);
        if (!string.IsNullOrEmpty(OptionD)) options.Add(OptionD);
        return options;
    }
}
