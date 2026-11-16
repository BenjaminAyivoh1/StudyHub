using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using StudyHub.Api.Common;
using StudyHub.Api.Contracts;
using StudyHub.Api.Services;

namespace StudyHub.Api.Live;

public record GameCreated(string Code, string Title, int QuestionCount);

public record JoinedGame(string Code, string Title, int QuestionCount);

public record FinalResult(int Rank, int Players, int Score, IReadOnlyList<PlayerScore> Leaderboard);

/// <summary>
/// The live connection between the host's screen and the players' phones.
/// Each browser keeps a connection open, so the server can push messages to it instantly.
/// The rules live in <see cref="QuizGame"/>; this class only passes messages along.
///
/// Messages the server sends:
///   to the host:    PlayersChanged, AnswerProgress
///   to the players: QuestionStarted, YourResult, GameFinished, GameEnded
/// </summary>
public class QuizHub(GameRegistry games, IQuizService quizzes) : Hub
{
    // ----- host (must be logged in, and must own the quiz) -----

    [Authorize]
    public async Task<GameCreated> CreateGame(Guid quizId)
    {
        QuizResponse quiz;
        try
        {
            quiz = await quizzes.GetAsync(Context.User!.GetUserId(), quizId);
        }
        catch (AppException ex)
        {
            throw new HubException(ex.Message);
        }

        var questions = quiz.Questions.Select(q => new GameQuestion(q.Text, q.Options, q.CorrectIndex, q.Seconds));
        var game = games.Create(Context.ConnectionId, quiz.Title, questions, DateTime.UtcNow);

        await Groups.AddToGroupAsync(Context.ConnectionId, game.Code);
        return new GameCreated(game.Code, game.Title, game.QuestionCount);
    }

    public async Task<QuestionView> StartQuestion(string code)
    {
        var game = HostGame(code);
        var question = Run(() => game.StartNextQuestion(DateTime.UtcNow));

        await Clients.OthersInGroup(game.Code).SendAsync("QuestionStarted", question);
        return question;
    }

    public async Task<RevealView> RevealAnswer(string code)
    {
        var game = HostGame(code);
        var reveal = Run(() => game.Reveal());

        foreach (var connectionId in game.PlayerConnectionIds())
        {
            await Clients.Client(connectionId).SendAsync("YourResult", game.ResultFor(connectionId));
        }

        return reveal;
    }

    public async Task<IReadOnlyList<PlayerScore>> FinishGame(string code)
    {
        var game = HostGame(code);
        var leaderboard = game.Finish();
        var top = leaderboard.Take(5).ToList();

        foreach (var connectionId in game.PlayerConnectionIds())
        {
            var result = game.ResultFor(connectionId);
            await Clients.Client(connectionId)
                .SendAsync("GameFinished", new FinalResult(result.Rank, result.Players, result.Score, top));
        }

        games.Remove(game.Code);
        return leaderboard;
    }

    // ----- players (no account needed) -----

    public async Task<JoinedGame> JoinGame(string code, string name)
    {
        var game = Run(() => games.Require(code));
        Run(() => game.AddPlayer(Context.ConnectionId, name));

        games.Track(Context.ConnectionId, game.Code);
        await Groups.AddToGroupAsync(Context.ConnectionId, game.Code);
        await Clients.Client(game.HostConnectionId).SendAsync("PlayersChanged", game.PlayerNames());

        return new JoinedGame(game.Code, game.Title, game.QuestionCount);
    }

    public async Task SubmitAnswer(string code, int optionIndex)
    {
        var game = Run(() => games.Require(code));
        var progress = Run(() => game.SubmitAnswer(Context.ConnectionId, optionIndex, DateTime.UtcNow));

        await Clients.Client(game.HostConnectionId).SendAsync("AnswerProgress", progress);
    }

    // ----- someone closed their browser or lost their connection -----

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var game = games.FindByConnection(Context.ConnectionId);
        if (game is not null)
        {
            if (game.HostConnectionId == Context.ConnectionId)
            {
                // Without a host nobody can move the game on, so it ends for everyone.
                await Clients.OthersInGroup(game.Code).SendAsync("GameEnded", "The host left, so the game has ended.");
                games.Remove(game.Code);
            }
            else
            {
                games.Untrack(Context.ConnectionId);
                if (game.RemovePlayer(Context.ConnectionId))
                {
                    await Clients.Client(game.HostConnectionId).SendAsync("PlayersChanged", game.PlayerNames());
                }
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    // ----- helpers -----

    private QuizGame HostGame(string code)
    {
        var game = Run(() => games.Require(code));
        if (game.HostConnectionId != Context.ConnectionId)
        {
            throw new HubException("Only the host can do that.");
        }

        return game;
    }

    // Turns a broken game rule into a message the player's screen can show.
    private static T Run<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (GameException ex)
        {
            throw new HubException(ex.Message);
        }
    }

    private static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (GameException ex)
        {
            throw new HubException(ex.Message);
        }
    }
}
