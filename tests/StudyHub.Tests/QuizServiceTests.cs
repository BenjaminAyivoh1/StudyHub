using StudyHub.Api.Common;
using StudyHub.Api.Contracts;
using StudyHub.Api.Services;

namespace StudyHub.Tests;

public class QuizServiceTests
{
    private static QuizQuestionDto Question(
        string text = "Which scheduler can starve long jobs?",
        int correctIndex = 1,
        int seconds = 20,
        params string[] options)
        => new(text, options.Length > 0 ? options.ToList() : new List<string> { "FCFS", "SJF", "Round Robin" }, correctIndex, seconds);

    private static QuizRequest Quiz(string title = "Operating systems quiz", params QuizQuestionDto[] questions)
        => new(title, questions.Length > 0 ? questions.ToList() : new List<QuizQuestionDto> { Question() });

    [Fact]
    public async Task Creating_a_quiz_saves_its_questions_in_order()
    {
        await using var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var service = new QuizService(db);

        var created = await service.CreateAsync(owner.Id, Quiz("  Operating systems quiz  ",
            Question("First question?", 0, 10, "Yes", "No"),
            Question("Second question?", 3, 30, "A", "B", "C", "D")));

        var loaded = await service.GetAsync(owner.Id, created.Id);

        Assert.Equal("Operating systems quiz", loaded.Title);
        Assert.Equal(2, loaded.Questions.Count);
        Assert.Equal("First question?", loaded.Questions[0].Text);
        Assert.Equal(new[] { "Yes", "No" }, loaded.Questions[0].Options);
        Assert.Equal(10, loaded.Questions[0].Seconds);
        Assert.Equal(new[] { "A", "B", "C", "D" }, loaded.Questions[1].Options);
        Assert.Equal(3, loaded.Questions[1].CorrectIndex);
    }

    [Fact]
    public async Task Only_the_owner_can_open_edit_or_delete_a_quiz()
    {
        await using var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var other = await TestDb.AddUserAsync(db, "kofi@example.com", "Kofi");
        var service = new QuizService(db);
        var quiz = await service.CreateAsync(owner.Id, Quiz());

        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetAsync(other.Id, quiz.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.UpdateAsync(other.Id, quiz.Id, Quiz("Hijacked quiz")));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.DeleteAsync(other.Id, quiz.Id));
    }

    [Fact]
    public async Task The_list_shows_only_my_quizzes_with_question_counts()
    {
        await using var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var other = await TestDb.AddUserAsync(db, "kofi@example.com", "Kofi");
        var service = new QuizService(db);
        await service.CreateAsync(owner.Id, Quiz("My quiz", Question(), Question("Another question?")));
        await service.CreateAsync(other.Id, Quiz("Someone else's quiz"));

        var mine = await service.ListMineAsync(owner.Id);

        var summary = Assert.Single(mine);
        Assert.Equal("My quiz", summary.Title);
        Assert.Equal(2, summary.QuestionCount);
    }

    [Fact]
    public async Task Updating_replaces_the_title_and_the_questions()
    {
        await using var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var service = new QuizService(db);
        var quiz = await service.CreateAsync(owner.Id, Quiz("Old title", Question("Old one?"), Question("Old two?")));

        await service.UpdateAsync(owner.Id, quiz.Id, Quiz("New title", Question("Only question now?")));

        var loaded = await service.GetAsync(owner.Id, quiz.Id);
        Assert.Equal("New title", loaded.Title);
        Assert.Equal("Only question now?", Assert.Single(loaded.Questions).Text);
        Assert.Single(db.QuizQuestions);
    }

    [Fact]
    public async Task Deleting_a_quiz_removes_it_and_its_questions()
    {
        await using var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var service = new QuizService(db);
        var quiz = await service.CreateAsync(owner.Id, Quiz());

        await service.DeleteAsync(owner.Id, quiz.Id);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(owner.Id, quiz.Id));
        Assert.Empty(db.QuizQuestions);
    }

    [Fact]
    public async Task A_quiz_needs_a_title_and_at_least_one_question()
    {
        await using var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var service = new QuizService(db);

        await Assert.ThrowsAsync<BadRequestException>(() => service.CreateAsync(owner.Id, Quiz("ab")));
        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.CreateAsync(owner.Id, new QuizRequest("A good title", new List<QuizQuestionDto>())));
    }

    [Fact]
    public async Task A_question_needs_two_to_four_answers_and_a_valid_correct_one()
    {
        await using var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var service = new QuizService(db);

        var oneAnswer = Quiz("Quiz", Question("Only one answer?", 0, 20, "Yes"));
        var fiveAnswers = Quiz("Quiz", Question("Too many?", 0, 20, "A", "B", "C", "D", "E"));
        var blankAnswer = Quiz("Quiz", Question("Blank answer?", 0, 20, "Yes", "   "));
        var correctOutOfRange = Quiz("Quiz", Question("Wrong index?", 2, 20, "Yes", "No"));
        var tooFast = Quiz("Quiz", Question("Too fast?", 0, 2, "Yes", "No"));

        await Assert.ThrowsAsync<BadRequestException>(() => service.CreateAsync(owner.Id, oneAnswer));
        await Assert.ThrowsAsync<BadRequestException>(() => service.CreateAsync(owner.Id, fiveAnswers));
        await Assert.ThrowsAsync<BadRequestException>(() => service.CreateAsync(owner.Id, blankAnswer));
        await Assert.ThrowsAsync<BadRequestException>(() => service.CreateAsync(owner.Id, correctOutOfRange));
        var error = await Assert.ThrowsAsync<BadRequestException>(() => service.CreateAsync(owner.Id, tooFast));

        Assert.StartsWith("Question 1:", error.Message);
        Assert.Empty(db.Quizzes);
    }
}
