using Microsoft.EntityFrameworkCore;
using StudyHub.Api.Common;
using StudyHub.Api.Contracts;
using StudyHub.Api.Data;
using StudyHub.Api.Domain;
using StudyHub.Api.Live;

namespace StudyHub.Api.Services;

public interface IGameHistoryService
{
    /// <summary>Saves a finished game. Players who weren't logged in are saved by nickname only.</summary>
    Task SaveAsync(Guid quizId, Guid hostId, string quizTitle, GameSummary summary, CancellationToken ct = default);

    /// <summary>The games a logged-in student played, newest first.</summary>
    Task<IReadOnlyList<HistoryItem>> ListMineAsync(Guid userId, CancellationToken ct = default);

    /// <summary>How a quiz has been played. Only the student who wrote it can see this.</summary>
    Task<QuizStatsResponse> QuizStatsAsync(Guid userId, Guid quizId, CancellationToken ct = default);
}

public class GameHistoryService(AppDbContext db) : IGameHistoryService
{
    private const int MaxHistory = 100;
    private const int MaxSessionsShown = 20;

    public async Task SaveAsync(
        Guid quizId, Guid hostId, string quizTitle, GameSummary summary, CancellationToken ct = default)
    {
        if (hostId == Guid.Empty || summary.QuestionsPlayed == 0 || summary.Players.Count == 0)
        {
            return; // nothing worth saving
        }

        // The quiz may have been deleted while the game was running.
        var quizStillExists = await db.Quizzes.AnyAsync(q => q.Id == quizId, ct);

        // Only link results to accounts that still exist.
        var claimedIds = summary.Players
            .Where(p => p.UserId is not null)
            .Select(p => p.UserId!.Value)
            .Distinct()
            .ToList();
        var realIds = (await db.Users
                .Where(u => claimedIds.Contains(u.Id))
                .Select(u => u.Id)
                .ToListAsync(ct))
            .ToHashSet();

        var title = (quizTitle ?? string.Empty).Trim();
        var session = new GameSession
        {
            QuizId = quizStillExists ? quizId : null,
            QuizTitle = title.Length > 200 ? title[..200] : title,
            HostId = hostId,
            CorrectOnly = summary.CorrectOnly,
            QuestionsPlayed = summary.QuestionsPlayed,
            PlayedAtUtc = DateTime.UtcNow
        };

        foreach (var player in summary.Players)
        {
            session.Results.Add(new GameResult
            {
                UserId = player.UserId is Guid id && realIds.Contains(id) ? id : null,
                Nickname = player.Name,
                Score = player.Score,
                CorrectCount = player.Correct,
                Rank = player.Rank
            });
        }

        foreach (var question in summary.Questions)
        {
            session.QuestionStats.Add(new GameQuestionStat
            {
                Position = question.Position,
                QuestionText = question.Text,
                Asked = question.Asked,
                Answered = question.Answered,
                Correct = question.Correct
            });
        }

        db.GameSessions.Add(session);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<HistoryItem>> ListMineAsync(Guid userId, CancellationToken ct = default)
    {
        return await db.GameResults
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.Session.PlayedAtUtc)
            .Take(MaxHistory)
            .Select(r => new HistoryItem(
                r.SessionId,
                r.Session.QuizTitle,
                r.Session.PlayedAtUtc,
                r.Score,
                r.CorrectCount,
                r.Session.QuestionsPlayed,
                r.Rank,
                r.Session.Results.Count,
                r.Session.CorrectOnly))
            .ToListAsync(ct);
    }

    public async Task<QuizStatsResponse> QuizStatsAsync(Guid userId, Guid quizId, CancellationToken ct = default)
    {
        var quiz = await db.Quizzes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == quizId, ct)
            ?? throw new NotFoundException("Quiz not found.");

        if (quiz.OwnerId != userId)
        {
            throw new ForbiddenException("You can only see stats for your own quizzes.");
        }

        var sessions = await db.GameSessions
            .AsNoTracking()
            .Where(s => s.QuizId == quizId)
            .Include(s => s.Results)
            .Include(s => s.QuestionStats)
            .OrderByDescending(s => s.PlayedAtUtc)
            .ToListAsync(ct);

        var results = sessions.SelectMany(s => s.Results.Select(r => (Session: s, Result: r))).ToList();

        // Average share of questions answered correctly, per player per game.
        var scored = results.Where(x => x.Session.QuestionsPlayed > 0).ToList();
        var averageCorrect = scored.Count == 0
            ? 0
            : (int)Math.Round(scored.Average(x => 100.0 * x.Result.CorrectCount / x.Session.QuestionsPlayed));

        var recent = sessions
            .Take(MaxSessionsShown)
            .Select(s =>
            {
                var winner = s.Results
                    .OrderBy(r => r.Rank)
                    .ThenBy(r => r.Nickname, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                return new SessionSummary(
                    s.Id, s.PlayedAtUtc, s.Results.Count, winner?.Nickname, winner?.Score ?? 0, s.CorrectOnly, s.QuestionsPlayed);
            })
            .ToList();

        // Questions are matched by their text, so stats survive the quiz being reordered.
        // The hardest questions (lowest share of right answers) come first.
        var questions = sessions
            .SelectMany(s => s.QuestionStats)
            .GroupBy(q => q.QuestionText)
            .Select(g =>
            {
                var asked = g.Sum(q => q.Asked);
                var correct = g.Sum(q => q.Correct);
                var percent = asked == 0 ? 0 : (int)Math.Round(100.0 * correct / asked);
                return new QuestionStatResponse(g.Key, asked, g.Sum(q => q.Answered), correct, percent);
            })
            .OrderBy(q => q.CorrectPercent)
            .ThenBy(q => q.Text, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new QuizStatsResponse(
            quiz.Id,
            quiz.Title,
            sessions.Count,
            results.Count,
            results.Where(x => x.Result.UserId is not null).Select(x => x.Result.UserId).Distinct().Count(),
            averageCorrect,
            sessions.FirstOrDefault()?.PlayedAtUtc,
            recent,
            questions);
    }
}
