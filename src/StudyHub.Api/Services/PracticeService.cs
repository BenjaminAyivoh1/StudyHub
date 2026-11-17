using Microsoft.EntityFrameworkCore;
using StudyHub.Api.Common;
using StudyHub.Api.Contracts;
using StudyHub.Api.Data;
using StudyHub.Api.Live;
using StudyHub.Api.Practice;

namespace StudyHub.Api.Services;

public interface IPracticeService
{
    Task<IReadOnlyList<PublicQuizSummary>> ListPublicAsync(string? topic, string? search, CancellationToken ct = default);
    Task<IReadOnlyList<string>> TopicsAsync(CancellationToken ct = default);
    Task<PracticeStartResponse> StartAsync(Guid? userId, PracticeStartRequest request, CancellationToken ct = default);
    PracticeAnswerResponse Answer(Guid? userId, Guid practiceId, PracticeAnswerRequest request);
    Task<PracticeFinishResponse> FinishAsync(Guid? userId, Guid practiceId, CancellationToken ct = default);
}

/// <summary>
/// Solo practice without a host. Students can practise any shared quiz, their own quizzes,
/// or a random mix of shared questions. Logged-in students' runs are saved to their history.
/// </summary>
public class PracticeService(
    AppDbContext db, PracticeRegistry registry, IGameHistoryService history, Random? random = null) : IPracticeService
{
    public const int MinCount = 3;
    public const int MaxCount = 20;
    public const int DefaultCount = 10;
    private const int MaxListed = 100;
    private const int RandomPool = 500;

    private readonly Random rng = random ?? Random.Shared;

    public async Task<IReadOnlyList<PublicQuizSummary>> ListPublicAsync(
        string? topic, string? search, CancellationToken ct = default)
    {
        var query = db.Quizzes.AsNoTracking().Where(q => q.IsPublic && q.Questions.Count > 0);

        var wantedTopic = QuizService.NormalizeTopic(topic);
        if (wantedTopic is not null)
        {
            query = query.Where(q => q.Topic == wantedTopic);
        }

        var text = (search ?? string.Empty).Trim().ToLower();
        if (text.Length > 0)
        {
            query = query.Where(q => q.Title.ToLower().Contains(text));
        }

        return await query
            .OrderByDescending(q => q.CreatedAtUtc)
            .Take(MaxListed)
            .Select(q => new PublicQuizSummary(q.Id, q.Title, q.Topic, q.Questions.Count, q.Owner.DisplayName))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<string>> TopicsAsync(CancellationToken ct = default)
    {
        return await db.Quizzes
            .AsNoTracking()
            .Where(q => q.IsPublic && q.Topic != null && q.Questions.Count > 0)
            .Select(q => q.Topic!)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync(ct);
    }

    public async Task<PracticeStartResponse> StartAsync(
        Guid? userId, PracticeStartRequest request, CancellationToken ct = default)
    {
        PracticeRun run;
        var now = DateTime.UtcNow;

        if (request.QuizId is Guid quizId)
        {
            var quiz = await db.Quizzes
                .AsNoTracking()
                .Include(q => q.Questions)
                .FirstOrDefaultAsync(q => q.Id == quizId, ct);

            // A private quiz looks the same as a missing one to anybody but its writer.
            if (quiz is null || (!quiz.IsPublic && quiz.OwnerId != userId))
            {
                throw new NotFoundException("Quiz not found, or it isn't shared for practice.");
            }

            var questions = quiz.Questions
                .OrderBy(q => q.Position)
                .Select(q => new GameQuestion(q.Text, q.GetOptions(), q.CorrectIndex, q.Seconds))
                .ToList();

            run = new PracticeRun(userId, quiz.Id, quiz.Title, questions, now);
        }
        else
        {
            var topic = QuizService.NormalizeTopic(request.Topic);
            var count = Math.Clamp(request.Count ?? DefaultCount, MinCount, MaxCount);

            var pool = db.QuizQuestions.AsNoTracking().Where(q => q.Quiz.IsPublic);
            if (topic is not null)
            {
                pool = pool.Where(q => q.Quiz.Topic == topic);
            }

            var candidates = await pool
                .OrderByDescending(q => q.Quiz.CreatedAtUtc)
                .ThenBy(q => q.Position)
                .Take(RandomPool)
                .ToListAsync(ct);

            if (candidates.Count == 0)
            {
                throw new BadRequestException(topic is null
                    ? "Nobody has shared a quiz for practice yet."
                    : $"No shared questions on \"{topic}\" yet. Try another topic.");
            }

            // The same question can appear in two quizzes; keep one copy of each.
            var picked = candidates
                .GroupBy(q => q.Text.Trim().ToLowerInvariant())
                .Select(g => g.First())
                .OrderBy(_ => rng.Next())
                .Take(count)
                .Select(q => new GameQuestion(q.Text, q.GetOptions(), q.CorrectIndex, q.Seconds))
                .ToList();

            var title = topic is null ? "Random practice: all topics" : $"Random practice: {topic}";
            run = new PracticeRun(userId, null, title, picked, now);
        }

        registry.Add(run, now);

        var views = run.Questions
            .Select((q, i) => new PracticeQuestionView(i + 1, q.Text, q.Options))
            .ToList();
        return new PracticeStartResponse(run.Id, run.Title, views);
    }

    public PracticeAnswerResponse Answer(Guid? userId, Guid practiceId, PracticeAnswerRequest request)
    {
        var run = FindOwnRun(userId, practiceId);
        var (correct, correctIndex, score, answered) = run.Answer(request.Question, request.Option);
        return new PracticeAnswerResponse(correct, correctIndex, score, answered, run.Questions.Count);
    }

    public async Task<PracticeFinishResponse> FinishAsync(Guid? userId, Guid practiceId, CancellationToken ct = default)
    {
        var run = FindOwnRun(userId, practiceId);
        var outcomes = run.Finish();
        registry.Remove(run.Id);

        var saved = false;
        if (run.UserId is Guid id && outcomes.Any(o => o.Answered))
        {
            await history.SavePracticeAsync(id, run.QuizId, run.Title, outcomes, ct);
            saved = true;
        }

        return new PracticeFinishResponse(outcomes.Count(o => o.Correct), outcomes.Count, saved);
    }

    /// <summary>Someone else's run looks the same as a missing one.</summary>
    private PracticeRun FindOwnRun(Guid? userId, Guid practiceId)
    {
        var run = registry.Find(practiceId, DateTime.UtcNow);
        if (run is null || run.UserId != userId)
        {
            throw new NotFoundException("Practice run not found. It may have expired; start a new one.");
        }

        return run;
    }
}
