using StudyHub.Api.Common;
using StudyHub.Api.Contracts;
using StudyHub.Api.Data;
using StudyHub.Api.Domain;
using StudyHub.Api.Live;
using StudyHub.Api.Services;

namespace StudyHub.Tests;

public class GameHistoryTests
{
    private static readonly DateTime Start = new(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);

    private static async Task<QuizResponse> MakeQuiz(AppDbContext db, Guid ownerId, string title = "Operating systems recap")
        => await new QuizService(db).CreateAsync(ownerId, new QuizRequest(title, new List<QuizQuestionDto>
        {
            new("Which scheduler can starve long jobs?", new List<string> { "FCFS", "SJF" }, 1, 20),
            new("Is Round Robin preemptive?", new List<string> { "Yes", "No" }, 0, 20)
        }));

    /// <summary>Plays a two-question game: Ama gets both right, Kofi gets one right, the guest gets none.</summary>
    private static QuizGame PlayGame(QuizResponse quiz, Guid hostId, Guid? amaId, Guid? kofiId)
    {
        var questions = quiz.Questions.Select(q => new GameQuestion(q.Text, q.Options, q.CorrectIndex, q.Seconds));
        var game = new QuizGame("ABC234", "host", quiz.Title, questions, Start) { QuizId = quiz.Id, HostUserId = hostId };
        game.AddPlayer("ama", "Ama", amaId);
        game.AddPlayer("kofi", "Kofi", kofiId);
        game.AddPlayer("guest", "Guest");

        game.StartNextQuestion(Start);
        game.SubmitAnswer("ama", 1, Start);
        game.SubmitAnswer("kofi", 0, Start);
        game.SubmitAnswer("guest", 0, Start);
        game.Reveal();

        var second = Start.AddMinutes(1);
        game.StartNextQuestion(second);
        game.SubmitAnswer("ama", 0, second.AddSeconds(10));
        game.SubmitAnswer("kofi", 0, second);
        game.Reveal();
        game.Finish();
        return game;
    }

    [Fact]
    public void The_game_summary_records_accounts_right_answers_and_each_question()
    {
        var amaId = Guid.NewGuid();
        var questions = new[]
        {
            new GameQuestion("Q1?", new[] { "A", "B" }, 1, 20),
            new GameQuestion("Q2?", new[] { "A", "B" }, 0, 20)
        };
        var game = new QuizGame("ABC234", "host", "Quiz", questions, Start);
        game.AddPlayer("ama", "Ama", amaId);
        game.AddPlayer("guest", "Guest");

        game.StartNextQuestion(Start);
        game.SubmitAnswer("ama", 1, Start);
        game.Reveal();

        var summary = game.Summary();

        Assert.Equal(1, summary.QuestionsPlayed);
        Assert.False(summary.CorrectOnly);
        var ama = summary.Players[0];
        Assert.Equal("Ama", ama.Name);
        Assert.Equal(amaId, ama.UserId);
        Assert.Equal(1, ama.Correct);
        Assert.Equal(1, ama.Rank);
        Assert.Null(summary.Players[1].UserId);
        Assert.Equal(0, summary.Players[1].Correct);
        Assert.Equal(new QuestionSummary(0, "Q1?", 2, 1, 1), Assert.Single(summary.Questions));
    }

    [Fact]
    public void The_same_account_cannot_join_one_game_twice()
    {
        var amaId = Guid.NewGuid();
        var game = new QuizGame("ABC234", "host", "Quiz", new[] { new GameQuestion("Q?", new[] { "A", "B" }, 0, 20) }, Start);
        game.AddPlayer("tab-1", "Ama", amaId);

        Assert.Throws<GameException>(() => game.AddPlayer("tab-2", "Ama again", amaId));
    }

    [Fact]
    public async Task A_saved_game_shows_in_each_signed_in_players_history()
    {
        await using var db = TestDb.Create();
        var host = await TestDb.AddUserAsync(db, "host@example.com", "Host");
        var ama = await TestDb.AddUserAsync(db, "ama@example.com", "Ama");
        var kofi = await TestDb.AddUserAsync(db, "kofi@example.com", "Kofi");
        var quiz = await MakeQuiz(db, host.Id);
        var game = PlayGame(quiz, host.Id, ama.Id, kofi.Id);
        var history = new GameHistoryService(db);

        await history.SaveAsync(quiz.Id, host.Id, quiz.Title, game.Summary());

        var amaItem = Assert.Single(await history.ListMineAsync(ama.Id));
        Assert.Equal("Operating systems recap", amaItem.QuizTitle);
        Assert.Equal(2, amaItem.CorrectCount);
        Assert.Equal(2, amaItem.QuestionsPlayed);
        Assert.Equal(1, amaItem.Rank);
        Assert.Equal(3, amaItem.Players);

        var kofiItem = Assert.Single(await history.ListMineAsync(kofi.Id));
        Assert.Equal(1, kofiItem.CorrectCount);
        Assert.Equal(2, kofiItem.Rank);

        // The host didn't play, so the game isn't in the host's own history.
        Assert.Empty(await history.ListMineAsync(host.Id));
        Assert.Equal(3, db.GameResults.Count());
        Assert.Single(db.GameResults.Where(r => r.UserId == null));
    }

    [Fact]
    public async Task The_quiz_writer_sees_how_their_quiz_has_been_played()
    {
        await using var db = TestDb.Create();
        var host = await TestDb.AddUserAsync(db, "host@example.com", "Host");
        var ama = await TestDb.AddUserAsync(db, "ama@example.com", "Ama");
        var quiz = await MakeQuiz(db, host.Id);
        var history = new GameHistoryService(db);
        await history.SaveAsync(quiz.Id, host.Id, quiz.Title, PlayGame(quiz, host.Id, ama.Id, null).Summary());
        await history.SaveAsync(quiz.Id, host.Id, quiz.Title, PlayGame(quiz, host.Id, ama.Id, null).Summary());

        var stats = await history.QuizStatsAsync(host.Id, quiz.Id);

        Assert.Equal(2, stats.TimesPlayed);
        Assert.Equal(6, stats.TotalPlayers);
        Assert.Equal(1, stats.SignedInPlayers); // only Ama had an account
        Assert.Equal(50, stats.AverageCorrectPercent); // 2/2, 1/2 and 0/2 right, in each game
        Assert.NotNull(stats.LastPlayedAtUtc);
        Assert.Equal(2, stats.Sessions.Count);
        Assert.Equal("Ama", stats.Sessions[0].Winner);
        Assert.Equal(3, stats.Sessions[0].Players);

        // Hardest question first: 1 in 3 got question 1 right, 2 in 3 got question 2.
        Assert.Equal("Which scheduler can starve long jobs?", stats.Questions[0].Text);
        Assert.Equal(33, stats.Questions[0].CorrectPercent);
        Assert.Equal(6, stats.Questions[0].Asked);
        Assert.Equal(67, stats.Questions[1].CorrectPercent);

        var list = Assert.Single(await new QuizService(db).ListMineAsync(host.Id));
        Assert.Equal(2, list.TimesPlayed);
    }

    [Fact]
    public async Task Only_the_quiz_writer_can_see_its_stats()
    {
        await using var db = TestDb.Create();
        var host = await TestDb.AddUserAsync(db, "host@example.com", "Host");
        var other = await TestDb.AddUserAsync(db, "kofi@example.com", "Kofi");
        var quiz = await MakeQuiz(db, host.Id);
        var history = new GameHistoryService(db);

        await Assert.ThrowsAsync<ForbiddenException>(() => history.QuizStatsAsync(other.Id, quiz.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => history.QuizStatsAsync(host.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task A_quiz_that_was_never_played_has_empty_stats()
    {
        await using var db = TestDb.Create();
        var host = await TestDb.AddUserAsync(db, "host@example.com", "Host");
        var quiz = await MakeQuiz(db, host.Id);

        var stats = await new GameHistoryService(db).QuizStatsAsync(host.Id, quiz.Id);

        Assert.Equal(0, stats.TimesPlayed);
        Assert.Equal(0, stats.AverageCorrectPercent);
        Assert.Null(stats.LastPlayedAtUtc);
        Assert.Empty(stats.Questions);
    }

    [Fact]
    public async Task History_survives_the_quiz_being_deleted()
    {
        await using var db = TestDb.Create();
        var host = await TestDb.AddUserAsync(db, "host@example.com", "Host");
        var ama = await TestDb.AddUserAsync(db, "ama@example.com", "Ama");
        var quiz = await MakeQuiz(db, host.Id);
        var history = new GameHistoryService(db);
        await history.SaveAsync(quiz.Id, host.Id, quiz.Title, PlayGame(quiz, host.Id, ama.Id, null).Summary());

        await new QuizService(db).DeleteAsync(host.Id, quiz.Id);

        Assert.Equal("Operating systems recap", Assert.Single(await history.ListMineAsync(ama.Id)).QuizTitle);
    }

    [Fact]
    public async Task Games_where_no_question_was_shown_are_not_saved()
    {
        await using var db = TestDb.Create();
        var host = await TestDb.AddUserAsync(db, "host@example.com", "Host");
        var quiz = await MakeQuiz(db, host.Id);
        var game = new QuizGame("ABC234", "host", quiz.Title,
            quiz.Questions.Select(q => new GameQuestion(q.Text, q.Options, q.CorrectIndex, q.Seconds)), Start);
        game.AddPlayer("ama", "Ama");

        await new GameHistoryService(db).SaveAsync(quiz.Id, host.Id, quiz.Title, game.Summary());

        Assert.Empty(db.GameSessions);
    }

    [Fact]
    public async Task Admin_totals_count_the_whole_site_without_the_team_account()
    {
        await using var db = TestDb.Create();
        await SampleData.SeedAsync(db, new StudyHub.Api.Security.Pbkdf2PasswordHasher());
        var host = await TestDb.AddUserAsync(db, "host@example.com", "Host");
        var ama = await TestDb.AddUserAsync(db, "ama@example.com", "Ama");
        var quiz = await MakeQuiz(db, host.Id);
        await new GameHistoryService(db).SaveAsync(quiz.Id, host.Id, quiz.Title, PlayGame(quiz, host.Id, ama.Id, null).Summary());

        // One older game that falls outside the last 7 days.
        db.GameSessions.Add(new GameSession
        {
            QuizTitle = "Old quiz",
            HostId = host.Id,
            QuestionsPlayed = 1,
            PlayedAtUtc = DateTime.UtcNow.AddDays(-30),
            Results = { new GameResult { Nickname = "Someone", Rank = 1 } }
        });
        await db.SaveChangesAsync();

        var stats = await new AdminService(db).GetStatsAsync(DateTime.UtcNow);

        Assert.Equal(2, stats.Users);
        Assert.Equal(15, stats.Notes);
        Assert.Equal(1, stats.Quizzes);
        Assert.Equal(2, stats.GamesPlayed);
        Assert.Equal(4, stats.GamePlayers);
        Assert.Equal(1, stats.GamesLast7Days);
        Assert.Equal(3, stats.PlayersLast7Days);
        Assert.Equal(0, stats.OpenReports);
    }
}
