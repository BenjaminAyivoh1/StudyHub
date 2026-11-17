using StudyHub.Api.Common;
using StudyHub.Api.Live;
using StudyHub.Api.Services;

namespace StudyHub.Api.Practice;

/// <summary>
/// One student's solo practice. The questions, with their right answers, stay on the server;
/// the browser only sends which option was picked and is told whether it was right.
/// </summary>
public class PracticeRun
{
    private readonly int?[] picks;
    private readonly object gate = new();

    public PracticeRun(Guid? userId, Guid? quizId, string title, IReadOnlyList<GameQuestion> questions, DateTime now)
    {
        if (questions.Count == 0)
        {
            throw new BadRequestException("There are no questions to practise.");
        }

        UserId = userId;
        QuizId = quizId;
        Title = title;
        Questions = questions;
        CreatedAtUtc = now;
        picks = new int?[questions.Count];
    }

    public Guid Id { get; } = Guid.NewGuid();
    public Guid? UserId { get; }
    public Guid? QuizId { get; }
    public string Title { get; }
    public IReadOnlyList<GameQuestion> Questions { get; }
    public DateTime CreatedAtUtc { get; }
    public bool Finished { get; private set; }

    public int Score
    {
        get { lock (gate) { return CountCorrect(); } }
    }

    public int Answered
    {
        get { lock (gate) { return picks.Count(p => p is not null); } }
    }

    /// <summary>Records an answer. Each question can only be answered once.</summary>
    public (bool Correct, int CorrectIndex, int Score, int Answered) Answer(int question, int option)
    {
        lock (gate)
        {
            if (Finished)
            {
                throw new ConflictException("This practice run has already finished.");
            }

            if (question < 0 || question >= Questions.Count)
            {
                throw new BadRequestException("That question isn't part of this practice run.");
            }

            var q = Questions[question];
            if (option < 0 || option >= q.Options.Count)
            {
                throw new BadRequestException("Pick one of the answers shown.");
            }

            if (picks[question] is not null)
            {
                throw new ConflictException("You've already answered this question.");
            }

            picks[question] = option;
            return (option == q.CorrectIndex, q.CorrectIndex, CountCorrect(), picks.Count(p => p is not null));
        }
    }

    /// <summary>Ends the run and returns how each question went. Can only happen once.</summary>
    public IReadOnlyList<PracticeOutcome> Finish()
    {
        lock (gate)
        {
            if (Finished)
            {
                throw new ConflictException("This practice run has already finished.");
            }

            Finished = true;
            return Questions
                .Select((q, i) => new PracticeOutcome(q.Text, picks[i] is not null, picks[i] == q.CorrectIndex))
                .ToList();
        }
    }

    private int CountCorrect()
        => picks.Where((p, i) => p is not null && p == Questions[i].CorrectIndex).Count();
}
