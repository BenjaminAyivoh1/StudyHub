using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace StudyHub.Api.Live;

/// <summary>
/// Keeps track of the games being played right now. Games live in memory only:
/// they disappear when they finish, when the host leaves, or when the server restarts.
/// </summary>
public class GameRegistry
{
    // Letters and digits that are easy to read aloud: no 0/O or 1/I.
    private const string CodeCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 6;
    private static readonly TimeSpan MaxGameAge = TimeSpan.FromHours(3);

    private readonly ConcurrentDictionary<string, QuizGame> _games = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _codeByConnection = new();

    public QuizGame Create(string hostConnectionId, string title, IEnumerable<GameQuestion> questions, DateTime now)
    {
        RemoveOldGames(now);

        while (true)
        {
            var game = new QuizGame(NewCode(), hostConnectionId, title, questions, now);
            if (_games.TryAdd(game.Code, game))
            {
                _codeByConnection[hostConnectionId] = game.Code;
                return game;
            }
        }
    }

    public QuizGame? Find(string? code)
    {
        var normalised = (code ?? string.Empty).Trim();
        return _games.TryGetValue(normalised, out var game) ? game : null;
    }

    public QuizGame Require(string? code)
        => Find(code) ?? throw new GameException("No game with that code. Check it and try again.");

    /// <summary>Remembers which game a player's connection belongs to, so we can tidy up if they leave.</summary>
    public void Track(string connectionId, string code) => _codeByConnection[connectionId] = code;

    public void Untrack(string connectionId) => _codeByConnection.TryRemove(connectionId, out _);

    public QuizGame? FindByConnection(string connectionId)
        => _codeByConnection.TryGetValue(connectionId, out var code) ? Find(code) : null;

    public void Remove(string code)
    {
        if (!_games.TryRemove(code, out var game))
        {
            return;
        }

        Untrack(game.HostConnectionId);
        foreach (var connectionId in game.PlayerConnectionIds())
        {
            Untrack(connectionId);
        }
    }

    private void RemoveOldGames(DateTime now)
    {
        foreach (var game in _games.Values)
        {
            if (now - game.CreatedAtUtc > MaxGameAge)
            {
                Remove(game.Code);
            }
        }
    }

    private static string NewCode()
    {
        return string.Create(CodeLength, 0, static (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = CodeCharacters[RandomNumberGenerator.GetInt32(CodeCharacters.Length)];
            }
        });
    }
}
