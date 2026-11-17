using System.Collections.Concurrent;

namespace StudyHub.Api.Practice;

/// <summary>Practice runs in progress, kept in memory. Old ones are cleared out after a few hours.</summary>
public class PracticeRegistry
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(3);

    private readonly ConcurrentDictionary<Guid, PracticeRun> runs = new();

    public void Add(PracticeRun run, DateTime now)
    {
        RemoveExpired(now);
        runs[run.Id] = run;
    }

    public PracticeRun? Find(Guid id, DateTime now)
    {
        if (!runs.TryGetValue(id, out var run))
        {
            return null;
        }

        if (now - run.CreatedAtUtc > Lifetime)
        {
            runs.TryRemove(id, out _);
            return null;
        }

        return run;
    }

    public void Remove(Guid id) => runs.TryRemove(id, out _);

    public int Count => runs.Count;

    private void RemoveExpired(DateTime now)
    {
        foreach (var (id, run) in runs)
        {
            if (now - run.CreatedAtUtc > Lifetime)
            {
                runs.TryRemove(id, out _);
            }
        }
    }
}
