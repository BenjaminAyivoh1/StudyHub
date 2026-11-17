namespace StudyHub.Api.Live;

public enum GamePhase
{
    Lobby,
    Question,
    Reveal,
    Finished
}

/// <summary>How right answers are scored. The host picks this in the lobby.</summary>
public enum ScoringMode
{
    /// <summary>500 points for a right answer, plus up to 500 more for answering quickly.</summary>
    Speed,

    /// <summary>One point for each right answer. Speed doesn't matter.</summary>
    CorrectOnly
}

/// <summary>A question as the game holds it, including the right answer.</summary>
public record GameQuestion(string Text, IReadOnlyList<string> Options, int CorrectIndex, int Seconds);

/// <summary>What players are shown when a question starts. It never includes the right answer.</summary>
public record QuestionView(int Number, int Total, string Text, IReadOnlyList<string> Options, int Seconds, bool SpeedCounts);

public record PlayerScore(string Name, int Score);

/// <summary>What the host is shown when a question closes.</summary>
public record RevealView(int CorrectIndex, IReadOnlyList<int> Counts, IReadOnlyList<PlayerScore> Leaderboard, bool IsLast);

/// <summary>What one player is shown when a question closes.</summary>
public record PlayerResult(bool Answered, bool Correct, int CorrectIndex, int Points, int Score, int Rank, int Players);

public record AnswerProgress(int Answered, int Players);

/// <summary>Something a player or host did that the game rules don't allow. The message is shown to them.</summary>
public class GameException(string message) : Exception(message);

/// <summary>
/// One live game: its players, the current question and the scores.
/// It holds no web or database code, so every rule can be tested directly.
/// The time is always passed in, which lets tests control the clock.
/// </summary>
public class QuizGame
{
    public const int MaxPlayers = 100;
    public const int MaxNameLength = 20;
    public const int BasePoints = 500;
    public const int SpeedBonus = 500;

    /// <summary>Answers that arrive this long after the timer ends still count, to allow for slow connections.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(1.5);

    private sealed class Player(string name)
    {
        public string Name { get; } = name;
        public int Score { get; set; }
        public int? Answer { get; set; }
        public int Points { get; set; }
    }

    private readonly object _gate = new();
    private readonly List<GameQuestion> _questions;
    private readonly Dictionary<string, Player> _players = new();
    private int _index = -1;
    private DateTime _questionStartedAt;

    public QuizGame(string code, string hostConnectionId, string title, IEnumerable<GameQuestion> questions, DateTime now)
    {
        Code = code;
        HostConnectionId = hostConnectionId;
        Title = title;
        CreatedAtUtc = now;
        _questions = questions.ToList();
    }

    public string Code { get; }
    public string HostConnectionId { get; }
    public string Title { get; }
    public DateTime CreatedAtUtc { get; }
    public GamePhase Phase { get; private set; } = GamePhase.Lobby;
    public ScoringMode Scoring { get; private set; } = ScoringMode.Speed;
    public int QuestionCount => _questions.Count;

    public IReadOnlyList<string> PlayerNames()
    {
        lock (_gate)
        {
            return _players.Values.Select(p => p.Name).ToList();
        }
    }

    public IReadOnlyList<string> PlayerConnectionIds()
    {
        lock (_gate)
        {
            return _players.Keys.ToList();
        }
    }

    /// <summary>The host can change how answers are scored, but only before the first question.</summary>
    public void SetScoring(ScoringMode mode)
    {
        lock (_gate)
        {
            if (Phase != GamePhase.Lobby)
            {
                throw new GameException("Scoring can only be changed before the quiz starts.");
            }

            Scoring = mode;
        }
    }

    public void AddPlayer(string connectionId, string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();

        lock (_gate)
        {
            if (Phase != GamePhase.Lobby)
            {
                throw new GameException("This game has already started.");
            }

            if (trimmed.Length is 0 or > MaxNameLength)
            {
                throw new GameException($"Enter a name of up to {MaxNameLength} characters.");
            }

            if (_players.Count >= MaxPlayers)
            {
                throw new GameException("This game is full.");
            }

            if (_players.Values.Any(p => string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                throw new GameException("That name is taken. Pick another.");
            }

            _players[connectionId] = new Player(trimmed);
        }
    }

    /// <summary>Returns true if that connection was a player in this game.</summary>
    public bool RemovePlayer(string connectionId)
    {
        lock (_gate)
        {
            return _players.Remove(connectionId);
        }
    }

    public QuestionView StartNextQuestion(DateTime now)
    {
        lock (_gate)
        {
            if (Phase is GamePhase.Question)
            {
                throw new GameException("Show the answer before moving on.");
            }

            if (Phase is GamePhase.Finished || _index + 1 >= _questions.Count)
            {
                throw new GameException("There are no more questions.");
            }

            if (_players.Count == 0)
            {
                throw new GameException("Wait for at least one player to join.");
            }

            _index++;
            _questionStartedAt = now;
            Phase = GamePhase.Question;

            foreach (var player in _players.Values)
            {
                player.Answer = null;
                player.Points = 0;
            }

            var question = _questions[_index];
            return new QuestionView(
                _index + 1, _questions.Count, question.Text, question.Options, question.Seconds, Scoring == ScoringMode.Speed);
        }
    }

    public AnswerProgress SubmitAnswer(string connectionId, int optionIndex, DateTime now)
    {
        lock (_gate)
        {
            if (Phase != GamePhase.Question)
            {
                throw new GameException("This question is closed.");
            }

            if (!_players.TryGetValue(connectionId, out var player))
            {
                throw new GameException("You are not in this game.");
            }

            if (player.Answer is not null)
            {
                throw new GameException("You have already answered.");
            }

            var question = _questions[_index];
            if (optionIndex < 0 || optionIndex >= question.Options.Count)
            {
                throw new GameException("That isn't one of the answers.");
            }

            var elapsed = now - _questionStartedAt;
            if (elapsed > TimeSpan.FromSeconds(question.Seconds) + Grace)
            {
                throw new GameException("Time is up.");
            }

            player.Answer = optionIndex;
            if (optionIndex == question.CorrectIndex)
            {
                player.Points = Scoring == ScoringMode.CorrectOnly ? 1 : PointsFor(elapsed, question.Seconds);
                player.Score += player.Points;
            }

            return new AnswerProgress(_players.Values.Count(p => p.Answer is not null), _players.Count);
        }
    }

    /// <summary>Closes the current question and reports how everyone answered.</summary>
    public RevealView Reveal()
    {
        lock (_gate)
        {
            if (Phase != GamePhase.Question)
            {
                throw new GameException("There is no open question.");
            }

            Phase = GamePhase.Reveal;

            var question = _questions[_index];
            var counts = new int[question.Options.Count];
            foreach (var player in _players.Values)
            {
                if (player.Answer is int answer)
                {
                    counts[answer]++;
                }
            }

            return new RevealView(question.CorrectIndex, counts, Leaderboard(5), _index == _questions.Count - 1);
        }
    }

    /// <summary>How one player did on the question that just closed, and where they stand.</summary>
    public PlayerResult ResultFor(string connectionId)
    {
        lock (_gate)
        {
            if (!_players.TryGetValue(connectionId, out var player))
            {
                throw new GameException("You are not in this game.");
            }

            // Before the first question there is no right answer yet.
            var correctIndex = _index >= 0 ? _questions[_index].CorrectIndex : -1;
            var rank = 1 + _players.Values.Count(p => p.Score > player.Score);

            return new PlayerResult(
                player.Answer is not null,
                player.Answer is int answer && answer == correctIndex,
                correctIndex,
                player.Points,
                player.Score,
                rank,
                _players.Count);
        }
    }

    /// <summary>Ends the game and returns the full leaderboard.</summary>
    public IReadOnlyList<PlayerScore> Finish()
    {
        lock (_gate)
        {
            Phase = GamePhase.Finished;
            return Leaderboard(_players.Count);
        }
    }

    /// <summary>
    /// A right answer earns 500 points, plus up to 500 more for speed:
    /// an instant answer gets the full bonus, and an answer at the last moment gets none.
    /// </summary>
    public static int PointsFor(TimeSpan elapsed, int seconds)
    {
        var fractionLeft = 1 - elapsed.TotalSeconds / seconds;
        fractionLeft = Math.Clamp(fractionLeft, 0, 1);
        return BasePoints + (int)Math.Round(SpeedBonus * fractionLeft);
    }

    private List<PlayerScore> Leaderboard(int take)
    {
        return _players.Values
            .OrderByDescending(p => p.Score)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .Select(p => new PlayerScore(p.Name, p.Score))
            .ToList();
    }
}
