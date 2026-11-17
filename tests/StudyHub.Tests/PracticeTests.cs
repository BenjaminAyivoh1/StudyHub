using StudyHub.Api.Common;
using StudyHub.Api.Contracts;
using StudyHub.Api.Data;
using StudyHub.Api.Live;
using StudyHub.Api.Practice;
using StudyHub.Api.Services;

namespace StudyHub.Tests;

public class PracticeTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);

    private static List<QuizQuestionDto> Questions(string prefix, int count)
        => Enumerable.Range(1, count)
            .Select(i => new QuizQuestionDto($"{prefix} question {i}?", new List<string> { "Right", "Wrong" }, 0, 20))
            .ToList();

    private static Task<QuizResponse> MakeQuiz(
        AppDbContext db, Guid ownerId, string title, bool isPublic, string? topic = null, int count = 3)
        => new QuizService(db).CreateAsync(ownerId, new QuizRequest(title, Questions(title, count), isPublic, topic));

    private static PracticeService Service(AppDbContext db, PracticeRegistry? registry = null)
        => new(db, registry ?? new PracticeRegistry(), new GameHistoryService(db), new Random(42));

    // ----- the run itself -----

    [Fact]
    public void A_practice_run_checks_each_answer_once_and_keeps_score()
    {
        var run = new PracticeRun(null, null, "Practice", new[]
        {
            new GameQuestion("Q1?", new[] { "A", "B" }, 1, 20),
            new GameQuestion("Q2?", new[] { "A", "B", "C" }, 2, 20)
        }, Now);

        var first = run.Answer(0, 1);
        Assert.True(first.Correct);
        Assert.Equal(1, first.Score);

        var second = run.Answer(1, 0);
        Assert.False(second.Correct);
        Assert.Equal(2, second.CorrectIndex);
        Assert.Equal(1, second.Score);
        Assert.Equal(2, second.Answered);

        Assert.Throws<ConflictException>(() => run.Answer(0, 0));
        Assert.Throws<BadRequestException>(() => run.Answer(5, 0));
        Assert.Throws<BadRequestException>(() => run.Answer(1, 3));
    }

    [Fact]
    public void Finishing_reports_each_question_and_cannot_happen_twice()
    {
        var run = new PracticeRun(null, null, "Practice", new[]
        {
            new GameQuestion("Q1?", new[] { "A", "B" }, 0, 20),
            new GameQuestion("Q2?", new[] { "A", "B" }, 0, 20)
        }, Now);
        run.Answer(0, 0);

        var outcomes = run.Finish();

        Assert.Equal(new PracticeOutcome("Q1?", true, true), outcomes[0]);
        Assert.Equal(new PracticeOutcome("Q2?", false, false), outcomes[1]);
        Assert.Throws<ConflictException>(() => run.Finish());
        Assert.Throws<ConflictException>(() => run.Answer(1, 0));
    }

    [Fact]
    public void Old_practice_runs_expire()
    {
        var registry = new PracticeRegistry();
        var run = new PracticeRun(null, null, "Practice", new[] { new GameQuestion("Q?", new[] { "A", "B" }, 0, 20) }, Now);
        registry.Add(run, Now);

        Assert.NotNull(registry.Find(run.Id, Now.AddHours(1)));
        Assert.Null(registry.Find(run.Id, Now.AddHours(4)));
    }

    // ----- what can be practised -----

    [Fact]
    public async Task Only_shared_quizzes_are_listed_and_answers_are_never_sent()
    {
        await using var db = TestDb.Create();
        var writer = await TestDb.AddUserAsync(db, "writer@example.com", "Writer");
        await MakeQuiz(db, writer.Id, "Shared SQL quiz", isPublic: true, topic: "Databases");
        await MakeQuiz(db, writer.Id, "Private quiz", isPublic: false);
        var service = Service(db);

        var listed = await service.ListPublicAsync(null, null);

        var only = Assert.Single(listed);
        Assert.Equal("Shared SQL quiz", only.Title);
        Assert.Equal("databases", only.Topic);
        Assert.Equal("Writer", only.AuthorName);
        Assert.Equal("databases", Assert.Single(await service.TopicsAsync()));
    }

    [Fact]
    public async Task Someone_elses_private_quiz_cannot_be_practised_but_your_own_can()
    {
        await using var db = TestDb.Create();
        var writer = await TestDb.AddUserAsync(db, "writer@example.com", "Writer");
        var other = await TestDb.AddUserAsync(db, "other@example.com", "Other");
        var quiz = await MakeQuiz(db, writer.Id, "Private quiz", isPublic: false);
        var service = Service(db);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.StartAsync(other.Id, new PracticeStartRequest(quiz.Id)));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.StartAsync(null, new PracticeStartRequest(quiz.Id)));

        var mine = await service.StartAsync(writer.Id, new PracticeStartRequest(quiz.Id));
        Assert.Equal("Private quiz", mine.Title);
        Assert.Equal(3, mine.Questions.Count);
    }

    [Fact]
    public async Task Random_practice_mixes_shared_questions_from_the_chosen_topic_only()
    {
        await using var db = TestDb.Create();
        var writer = await TestDb.AddUserAsync(db, "writer@example.com", "Writer");
        await MakeQuiz(db, writer.Id, "Networks", isPublic: true, topic: "networks", count: 6);
        await MakeQuiz(db, writer.Id, "Databases", isPublic: true, topic: "databases", count: 6);
        await MakeQuiz(db, writer.Id, "Secret networks", isPublic: false, topic: "networks", count: 6);
        var service = Service(db);

        var run = await service.StartAsync(null, new PracticeStartRequest(Topic: "Networks", Count: 5));

        Assert.Equal("Random practice: networks", run.Title);
        Assert.Equal(5, run.Questions.Count);
        Assert.All(run.Questions, q => Assert.StartsWith("Networks question", q.Text));
        Assert.Equal(5, run.Questions.Select(q => q.Text).Distinct().Count());
    }

    [Fact]
    public async Task Random_practice_says_so_when_there_is_nothing_to_practise()
    {
        await using var db = TestDb.Create();
        var service = Service(db);

        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.StartAsync(null, new PracticeStartRequest(Topic: "physics")));
        Assert.Contains("physics", error.Message);
    }

    [Fact]
    public async Task The_number_of_random_questions_is_kept_between_3_and_20()
    {
        await using var db = TestDb.Create();
        var writer = await TestDb.AddUserAsync(db, "writer@example.com", "Writer");
        await MakeQuiz(db, writer.Id, "Big quiz", isPublic: true, count: 25);
        var service = Service(db);

        var tiny = await service.StartAsync(null, new PracticeStartRequest(Count: 1));
        var huge = await service.StartAsync(null, new PracticeStartRequest(Count: 100));

        Assert.Equal(PracticeService.MinCount, tiny.Questions.Count);
        Assert.Equal(PracticeService.MaxCount, huge.Questions.Count);
    }

    // ----- answering and saving -----

    [Fact]
    public async Task A_logged_in_students_run_is_saved_to_their_history_as_practice()
    {
        await using var db = TestDb.Create();
        var writer = await TestDb.AddUserAsync(db, "writer@example.com", "Writer");
        var ama = await TestDb.AddUserAsync(db, "ama@example.com", "Ama");
        var quiz = await MakeQuiz(db, writer.Id, "Shared quiz", isPublic: true);
        var service = Service(db);

        var run = await service.StartAsync(ama.Id, new PracticeStartRequest(quiz.Id));
        Assert.True(service.Answer(ama.Id, run.PracticeId, new PracticeAnswerRequest(0, 0)).Correct);
        Assert.False(service.Answer(ama.Id, run.PracticeId, new PracticeAnswerRequest(1, 1)).Correct);
        var done = await service.FinishAsync(ama.Id, run.PracticeId);

        Assert.Equal(1, done.Score);
        Assert.Equal(3, done.Total);
        Assert.True(done.Saved);

        var item = Assert.Single(await new GameHistoryService(db).ListMineAsync(ama.Id));
        Assert.True(item.IsPractice);
        Assert.Equal("Shared quiz", item.QuizTitle);
        Assert.Equal(1, item.CorrectCount);
        Assert.Equal(3, item.QuestionsPlayed);
        Assert.Equal(1, item.Players);
    }

    [Fact]
    public async Task A_guest_can_practise_but_nothing_is_saved()
    {
        await using var db = TestDb.Create();
        var writer = await TestDb.AddUserAsync(db, "writer@example.com", "Writer");
        var quiz = await MakeQuiz(db, writer.Id, "Shared quiz", isPublic: true);
        var service = Service(db);

        var run = await service.StartAsync(null, new PracticeStartRequest(quiz.Id));
        service.Answer(null, run.PracticeId, new PracticeAnswerRequest(0, 0));
        var done = await service.FinishAsync(null, run.PracticeId);

        Assert.Equal(1, done.Score);
        Assert.False(done.Saved);
        Assert.Equal(0, db.GameSessions.Count());
    }

    [Fact]
    public async Task Nobody_else_can_answer_or_finish_your_practice_run()
    {
        await using var db = TestDb.Create();
        var writer = await TestDb.AddUserAsync(db, "writer@example.com", "Writer");
        var ama = await TestDb.AddUserAsync(db, "ama@example.com", "Ama");
        var quiz = await MakeQuiz(db, writer.Id, "Shared quiz", isPublic: true);
        var service = Service(db);

        var run = await service.StartAsync(ama.Id, new PracticeStartRequest(quiz.Id));

        Assert.Throws<NotFoundException>(() => service.Answer(writer.Id, run.PracticeId, new PracticeAnswerRequest(0, 0)));
        Assert.Throws<NotFoundException>(() => service.Answer(null, run.PracticeId, new PracticeAnswerRequest(0, 0)));
        await Assert.ThrowsAsync<NotFoundException>(() => service.FinishAsync(writer.Id, run.PracticeId));
    }

    [Fact]
    public async Task Practice_runs_show_separately_from_live_games_in_quiz_and_site_stats()
    {
        await using var db = TestDb.Create();
        var writer = await TestDb.AddUserAsync(db, "writer@example.com", "Writer");
        var ama = await TestDb.AddUserAsync(db, "ama@example.com", "Ama");
        var quiz = await MakeQuiz(db, writer.Id, "Shared quiz", isPublic: true);
        var service = Service(db);

        var run = await service.StartAsync(ama.Id, new PracticeStartRequest(quiz.Id));
        service.Answer(ama.Id, run.PracticeId, new PracticeAnswerRequest(0, 1));
        await service.FinishAsync(ama.Id, run.PracticeId);

        var stats = await new GameHistoryService(db).QuizStatsAsync(writer.Id, quiz.Id);
        Assert.Equal(0, stats.TimesPlayed);
        Assert.Equal(1, stats.TimesPractised);
        Assert.Equal(0, stats.TotalPlayers);
        Assert.Empty(stats.Sessions);
        Assert.Equal(3, stats.Questions.Count);

        var mine = Assert.Single(await new QuizService(db).ListMineAsync(writer.Id));
        Assert.Equal(0, mine.TimesPlayed);
        Assert.True(mine.IsPublic);

        var site = await new AdminService(db).GetStatsAsync(DateTime.UtcNow);
        Assert.Equal(0, site.GamesPlayed);
        Assert.Equal(1, site.PracticeRuns);
    }

    [Fact]
    public async Task Topics_are_tidied_when_a_quiz_is_saved()
    {
        await using var db = TestDb.Create();
        var writer = await TestDb.AddUserAsync(db, "writer@example.com", "Writer");

        var quiz = await MakeQuiz(db, writer.Id, "Topic quiz", isPublic: true, topic: "  #Operating Systems ");
        var blank = await MakeQuiz(db, writer.Id, "No topic quiz", isPublic: false, topic: "   ");

        Assert.Equal("operating systems", quiz.Topic);
        Assert.Null(blank.Topic);
    }
}
