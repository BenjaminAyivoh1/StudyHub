using StudyHub.Api.Live;

namespace StudyHub.Tests;

public class QuizGameTests
{
    private static readonly DateTime Start = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private static QuizGame NewGame(int questions = 2)
    {
        var list = Enumerable.Range(1, questions)
            .Select(n => new GameQuestion($"Question {n}?", new[] { "A", "B", "C" }, 1, 20));
        return new QuizGame("ABC234", "host", "Test quiz", list, Start);
    }

    [Fact]
    public void Players_join_in_the_lobby_with_unique_names()
    {
        var game = NewGame();

        game.AddPlayer("c1", "  Ama  ");
        game.AddPlayer("c2", "Kofi");

        Assert.Equal(new[] { "Ama", "Kofi" }, game.PlayerNames());
        Assert.Throws<GameException>(() => game.AddPlayer("c3", "ama"));
        Assert.Throws<GameException>(() => game.AddPlayer("c3", "   "));
        Assert.Throws<GameException>(() => game.AddPlayer("c3", new string('x', 21)));
    }

    [Fact]
    public void Nobody_can_join_after_the_game_starts()
    {
        var game = NewGame();
        game.AddPlayer("c1", "Ama");
        game.StartNextQuestion(Start);

        Assert.Throws<GameException>(() => game.AddPlayer("c2", "Kofi"));
    }

    [Fact]
    public void A_game_needs_a_player_before_it_can_start()
    {
        var game = NewGame();

        Assert.Throws<GameException>(() => game.StartNextQuestion(Start));
        Assert.Equal(GamePhase.Lobby, game.Phase);
    }

    [Fact]
    public void Starting_a_question_shows_it_without_the_answer()
    {
        var game = NewGame(questions: 3);
        game.AddPlayer("c1", "Ama");

        var view = game.StartNextQuestion(Start);

        Assert.Equal(1, view.Number);
        Assert.Equal(3, view.Total);
        Assert.Equal("Question 1?", view.Text);
        Assert.Equal(new[] { "A", "B", "C" }, view.Options);
        Assert.Equal(GamePhase.Question, game.Phase);
    }

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(10, 750)]
    [InlineData(20, 500)]
    [InlineData(30, 500)]
    public void Faster_right_answers_earn_more_points(int secondsTaken, int expected)
    {
        Assert.Equal(expected, QuizGame.PointsFor(TimeSpan.FromSeconds(secondsTaken), 20));
    }

    [Fact]
    public void Right_answers_score_and_wrong_answers_do_not()
    {
        var game = NewGame();
        game.AddPlayer("c1", "Ama");
        game.AddPlayer("c2", "Kofi");
        game.AddPlayer("c3", "Esi");
        game.StartNextQuestion(Start);

        game.SubmitAnswer("c1", 1, Start.AddSeconds(5));
        var progress = game.SubmitAnswer("c2", 0, Start.AddSeconds(6));
        var reveal = game.Reveal();

        Assert.Equal(new AnswerProgress(2, 3), progress);
        Assert.Equal(1, reveal.CorrectIndex);
        Assert.Equal(new[] { 1, 1, 0 }, reveal.Counts);
        Assert.False(reveal.IsLast);
        Assert.Equal(new PlayerScore("Ama", 875), reveal.Leaderboard[0]);

        var ama = game.ResultFor("c1");
        var kofi = game.ResultFor("c2");
        var esi = game.ResultFor("c3");

        Assert.True(ama.Correct);
        Assert.Equal(875, ama.Points);
        Assert.Equal(1, ama.Rank);
        Assert.True(kofi.Answered);
        Assert.False(kofi.Correct);
        Assert.Equal(0, kofi.Score);
        Assert.Equal(2, kofi.Rank);
        Assert.False(esi.Answered);
        Assert.Equal(3, esi.Players);
    }

    [Fact]
    public void A_player_can_only_answer_once_and_only_with_a_real_option()
    {
        var game = NewGame();
        game.AddPlayer("c1", "Ama");
        game.StartNextQuestion(Start);

        Assert.Throws<GameException>(() => game.SubmitAnswer("c1", 7, Start.AddSeconds(1)));
        Assert.Throws<GameException>(() => game.SubmitAnswer("stranger", 1, Start.AddSeconds(1)));

        game.SubmitAnswer("c1", 0, Start.AddSeconds(2));

        Assert.Throws<GameException>(() => game.SubmitAnswer("c1", 1, Start.AddSeconds(3)));
        Assert.Equal(0, game.ResultFor("c1").Score);
    }

    [Fact]
    public void Answers_after_the_time_limit_are_refused()
    {
        var game = NewGame();
        game.AddPlayer("c1", "Ama");
        game.AddPlayer("c2", "Kofi");
        game.StartNextQuestion(Start);

        // One second late is inside the grace period; three seconds late is not.
        game.SubmitAnswer("c1", 1, Start.AddSeconds(21));

        Assert.Throws<GameException>(() => game.SubmitAnswer("c2", 1, Start.AddSeconds(23)));
        Assert.Equal(500, game.ResultFor("c1").Points);
    }

    [Fact]
    public void Answers_are_refused_once_the_answer_is_shown()
    {
        var game = NewGame();
        game.AddPlayer("c1", "Ama");
        game.StartNextQuestion(Start);
        game.Reveal();

        Assert.Throws<GameException>(() => game.SubmitAnswer("c1", 1, Start.AddSeconds(1)));
    }

    [Fact]
    public void Scores_add_up_across_questions_and_the_last_reveal_says_so()
    {
        var game = NewGame(questions: 2);
        game.AddPlayer("c1", "Ama");
        game.AddPlayer("c2", "Kofi");

        game.StartNextQuestion(Start);
        game.SubmitAnswer("c1", 1, Start);
        game.SubmitAnswer("c2", 1, Start.AddSeconds(20));
        Assert.Throws<GameException>(() => game.StartNextQuestion(Start.AddSeconds(21)));
        game.Reveal();

        var second = Start.AddMinutes(1);
        game.StartNextQuestion(second);
        game.SubmitAnswer("c2", 1, second);
        var reveal = game.Reveal();

        Assert.True(reveal.IsLast);
        Assert.Equal(new PlayerScore("Kofi", 1500), reveal.Leaderboard[0]);
        Assert.Equal(new PlayerScore("Ama", 1000), reveal.Leaderboard[1]);
        Assert.Throws<GameException>(() => game.StartNextQuestion(second.AddSeconds(30)));

        var final = game.Finish();

        Assert.Equal(GamePhase.Finished, game.Phase);
        Assert.Equal(new[] { "Kofi", "Ama" }, final.Select(p => p.Name));
        Assert.Equal(2, game.ResultFor("c1").Rank);
    }

    [Fact]
    public void A_player_who_leaves_is_removed()
    {
        var game = NewGame();
        game.AddPlayer("c1", "Ama");
        game.AddPlayer("c2", "Kofi");

        Assert.True(game.RemovePlayer("c1"));
        Assert.False(game.RemovePlayer("c1"));
        Assert.Equal(new[] { "Kofi" }, game.PlayerNames());
    }

    [Fact]
    public void The_registry_hands_out_codes_and_finds_games_by_code_or_connection()
    {
        var registry = new GameRegistry();
        var questions = new[] { new GameQuestion("Question?", new[] { "A", "B" }, 0, 20) };

        var game = registry.Create("host-1", "Quiz one", questions, Start);
        var other = registry.Create("host-2", "Quiz two", questions, Start);
        registry.Track("player-1", game.Code);

        Assert.Equal(6, game.Code.Length);
        Assert.NotEqual(game.Code, other.Code);
        Assert.Same(game, registry.Find($"  {game.Code.ToLowerInvariant()} "));
        Assert.Same(game, registry.FindByConnection("host-1"));
        Assert.Same(game, registry.FindByConnection("player-1"));
        Assert.Throws<GameException>(() => registry.Require("NOPE99"));

        registry.Remove(game.Code);

        Assert.Null(registry.Find(game.Code));
        Assert.Null(registry.FindByConnection("host-1"));
        Assert.Same(other, registry.Find(other.Code));
    }

    [Fact]
    public void Old_games_are_cleared_when_a_new_one_is_created()
    {
        var registry = new GameRegistry();
        var questions = new[] { new GameQuestion("Question?", new[] { "A", "B" }, 0, 20) };
        var old = registry.Create("host-1", "Old quiz", questions, Start);

        registry.Create("host-2", "New quiz", questions, Start.AddHours(4));

        Assert.Null(registry.Find(old.Code));
    }
}
