using Microsoft.EntityFrameworkCore;
using StudyHub.Api.Common;
using StudyHub.Api.Contracts;
using StudyHub.Api.Data;
using StudyHub.Api.Domain;

namespace StudyHub.Api.Services;

public interface IQuizService
{
    Task<IReadOnlyList<QuizSummary>> ListMineAsync(Guid userId, CancellationToken ct = default);
    Task<QuizResponse> GetAsync(Guid userId, Guid quizId, CancellationToken ct = default);
    Task<QuizResponse> CreateAsync(Guid userId, QuizRequest request, CancellationToken ct = default);
    Task<QuizResponse> UpdateAsync(Guid userId, Guid quizId, QuizRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, Guid quizId, CancellationToken ct = default);
}

/// <summary>Creating and editing quizzes. A quiz is private to the person who wrote it.</summary>
public class QuizService(AppDbContext db) : IQuizService
{
    public const int MaxQuestions = 30;
    public const int MinSeconds = 5;
    public const int MaxSeconds = 60;

    public async Task<IReadOnlyList<QuizSummary>> ListMineAsync(Guid userId, CancellationToken ct = default)
    {
        return await db.Quizzes
            .AsNoTracking()
            .Where(q => q.OwnerId == userId)
            .OrderByDescending(q => q.CreatedAtUtc)
            .Select(q => new QuizSummary(
                q.Id,
                q.Title,
                q.Questions.Count,
                q.CreatedAtUtc,
                db.GameSessions.Count(s => s.QuizId == q.Id && !s.IsPractice),
                q.IsPublic,
                q.Topic))
            .ToListAsync(ct);
    }

    public async Task<QuizResponse> GetAsync(Guid userId, Guid quizId, CancellationToken ct = default)
    {
        var quiz = await db.Quizzes
            .AsNoTracking()
            .Include(q => q.Questions)
            .FirstOrDefaultAsync(q => q.Id == quizId, ct)
            ?? throw new NotFoundException("Quiz not found.");

        EnsureOwner(quiz, userId);
        return ToResponse(quiz);
    }

    public async Task<QuizResponse> CreateAsync(Guid userId, QuizRequest request, CancellationToken ct = default)
    {
        var (title, questions) = Validate(request);

        var quiz = new Quiz
        {
            Title = title,
            OwnerId = userId,
            Questions = questions,
            IsPublic = request.IsPublic,
            Topic = NormalizeTopic(request.Topic)
        };
        db.Quizzes.Add(quiz);
        await db.SaveChangesAsync(ct);

        return ToResponse(quiz);
    }

    public async Task<QuizResponse> UpdateAsync(Guid userId, Guid quizId, QuizRequest request, CancellationToken ct = default)
    {
        var (title, questions) = Validate(request);

        var quiz = await db.Quizzes
            .Include(q => q.Questions)
            .FirstOrDefaultAsync(q => q.Id == quizId, ct)
            ?? throw new NotFoundException("Quiz not found.");

        EnsureOwner(quiz, userId);

        quiz.Title = title;
        quiz.IsPublic = request.IsPublic;
        quiz.Topic = NormalizeTopic(request.Topic);

        // Replace the old questions with the new ones. The new rows are added explicitly,
        // so the database is told to insert them rather than update rows that don't exist.
        db.QuizQuestions.RemoveRange(quiz.Questions);
        foreach (var question in questions)
        {
            question.QuizId = quiz.Id;
        }
        db.QuizQuestions.AddRange(questions);

        await db.SaveChangesAsync(ct);
        return new QuizResponse(quiz.Id, quiz.Title, ToDtos(questions), quiz.CreatedAtUtc, quiz.IsPublic, quiz.Topic);
    }

    public async Task DeleteAsync(Guid userId, Guid quizId, CancellationToken ct = default)
    {
        var quiz = await db.Quizzes
            .Include(q => q.Questions)
            .FirstOrDefaultAsync(q => q.Id == quizId, ct)
            ?? throw new NotFoundException("Quiz not found.");

        EnsureOwner(quiz, userId);

        db.Quizzes.Remove(quiz);
        await db.SaveChangesAsync(ct);
    }

    // ----- helpers -----

    private static void EnsureOwner(Quiz quiz, Guid userId)
    {
        if (quiz.OwnerId != userId)
        {
            throw new ForbiddenException("You can only use your own quizzes.");
        }
    }

    /// <summary>Checks a quiz and turns it into question rows. Every message says which question is wrong.</summary>
    private static (string Title, List<QuizQuestion> Questions) Validate(QuizRequest request)
    {
        var title = (request.Title ?? string.Empty).Trim();
        if (title.Length is < 3 or > 200)
        {
            throw new BadRequestException("Give the quiz a title between 3 and 200 characters.");
        }

        var items = request.Questions ?? new List<QuizQuestionDto>();
        if (items.Count == 0)
        {
            throw new BadRequestException("Add at least one question.");
        }

        if (items.Count > MaxQuestions)
        {
            throw new BadRequestException($"A quiz can have up to {MaxQuestions} questions.");
        }

        var questions = new List<QuizQuestion>();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var label = $"Question {i + 1}";

            var text = (item.Text ?? string.Empty).Trim();
            if (text.Length is < 3 or > 500)
            {
                throw new BadRequestException($"{label}: write the question (3 to 500 characters).");
            }

            var options = (item.Options ?? new List<string>())
                .Select(option => (option ?? string.Empty).Trim())
                .ToList();

            if (options.Count is < 2 or > 4)
            {
                throw new BadRequestException($"{label}: give between 2 and 4 answers.");
            }

            if (options.Any(option => option.Length is 0 or > 200))
            {
                throw new BadRequestException($"{label}: each answer needs 1 to 200 characters.");
            }

            if (item.CorrectIndex < 0 || item.CorrectIndex >= options.Count)
            {
                throw new BadRequestException($"{label}: choose which answer is correct.");
            }

            if (item.Seconds is < MinSeconds or > MaxSeconds)
            {
                throw new BadRequestException($"{label}: the time must be between {MinSeconds} and {MaxSeconds} seconds.");
            }

            questions.Add(new QuizQuestion
            {
                Position = i,
                Text = text,
                OptionA = options[0],
                OptionB = options[1],
                OptionC = options.Count > 2 ? options[2] : null,
                OptionD = options.Count > 3 ? options[3] : null,
                CorrectIndex = item.CorrectIndex,
                Seconds = item.Seconds
            });
        }

        return (title, questions);
    }

    private static QuizResponse ToResponse(Quiz quiz)
        => new(quiz.Id, quiz.Title, ToDtos(quiz.Questions), quiz.CreatedAtUtc, quiz.IsPublic, quiz.Topic);

    /// <summary>Topics are optional, trimmed, lower case and at most 50 characters, like note topics.</summary>
    public static string? NormalizeTopic(string? topic)
    {
        var value = (topic ?? string.Empty).Trim().TrimStart('#').ToLowerInvariant();
        if (value.Length == 0)
        {
            return null;
        }

        return value.Length > 50 ? value[..50] : value;
    }

    private static List<QuizQuestionDto> ToDtos(IEnumerable<QuizQuestion> questions)
        => questions
            .OrderBy(q => q.Position)
            .Select(q => new QuizQuestionDto(q.Text, q.GetOptions(), q.CorrectIndex, q.Seconds))
            .ToList();
}
