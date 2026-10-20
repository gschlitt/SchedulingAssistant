namespace TermPoint.Tests.Harness;

/// <summary>The result of shrinking one failing run.</summary>
/// <param name="Record">The shrunk decisions.</param>
/// <param name="Story">The printed story of the shrunk run, replayed once more so it is exactly what the record does.</param>
/// <param name="Events">The number of events in the shrunk run.</param>
/// <param name="Replays">How many times a candidate was replayed.</param>
/// <param name="Reproduced">False when even the unchanged record did not break the same invariant on replay (the run is not deterministic, so nothing was shrunk).</param>
internal sealed record ShrinkOutcome(RunRecord Record, string Story, int Events, int Replays, bool Reproduced);

/// <summary>
/// Shortens a failing run (spec item 9.3, step 9.3c). Given the decisions of a run and the breach it ended in, it
/// repeatedly tries a simpler run, replays it in a fresh world, and keeps the change only if the replay still breaks the
/// SAME invariant with the SAME finding (the same <see cref="FoundViolation.Signature"/>). It tries, until nothing more
/// can be removed:
/// <list type="number">
/// <item><description>cutting the run off just after the event that produced the breach;</description></item>
/// <item><description>removing events, in blocks that shrink from half the run down to one event (an event goes with the dialog answers given inside it);</description></item>
/// <item><description>shortening an event's duration (10 minutes to 4 minutes to 30 seconds);</description></item>
/// <item><description>replacing a dialog answer with a simpler one (an earlier option in its list).</description></item>
/// </list>
/// The result is a short story a person can read.
/// </summary>
internal static class Shrinker
{
    /// <summary>The most candidates one shrink replays before settling for what it has.</summary>
    public const int MaxReplays = 600;

    /// <summary>Shrinks a failing run.</summary>
    /// <param name="failing">The decisions of the failing run (cut off at the event that produced the breach, or longer).</param>
    /// <param name="signature">The breach to keep reproducing (<see cref="FoundViolation.Signature"/>).</param>
    /// <param name="options">The options every replay runs with (a planted bug is installed through them).</param>
    /// <returns>The shrunk run and its story.</returns>
    public static async Task<ShrinkOutcome> ShrinkAsync(RunRecord failing, string signature, RunOptions options)
    {
        var replays = 0;

        // Replays a candidate. When it breaks the same invariant, returns the run cut off after the event that did it.
        async Task<RunRecord?> TryAsync(RunRecord candidate)
        {
            replays++;
            var result = await RunExecutor.ExecuteAsync(new ReplaySource(candidate), options);
            var hit = result.Violations.FirstOrDefault(v => v.Signature == signature);
            return hit is null ? null : result.Record.Take(hit.EventIndex);
        }

        var current = await TryAsync(failing);
        if (current is null)
        {
            var again = await RunExecutor.ExecuteAsync(new ReplaySource(failing), options);
            return new ShrinkOutcome(failing, again.Story, failing.Count, replays, Reproduced: false);
        }

        var catalogue = EventCatalogue.Build().ToDictionary(e => e.Key);
        var progress = true;
        while (progress && replays < MaxReplays)
        {
            progress = false;

            // 1. Remove events, in blocks from half the run down to single events.
            for (var block = Math.Max(1, current.Count / 2); replays < MaxReplays; block /= 2)
            {
                var start = 0;
                while (start < current.Count && replays < MaxReplays)
                {
                    var length = Math.Min(block, current.Count - start);
                    if (length >= current.Count) { start += block; continue; }   // never remove everything

                    var smaller = await TryAsync(current.Without(start, length));
                    if (smaller is not null)
                    {
                        current  = smaller;
                        progress = true;       // the same start now names the events that followed the block
                    }
                    else
                    {
                        start += block;
                    }
                }
                if (block == 1) break;
            }

            // 2. Shorten durations and simplify answers, event by event.
            for (var i = 0; i < current.Count && replays < MaxReplays; i++)
            {
                while (replays < MaxReplays
                       && catalogue.TryGetValue(current.Steps[i].EventKey, out var ev)
                       && ev.ShorterKey is { } shorter)
                {
                    var candidate = current.Clone();
                    candidate.Steps[i] = new RecordedStep { EventKey = shorter, Answers = current.Steps[i].Answers.ToList() };
                    var accepted = await TryAsync(candidate);
                    if (accepted is null) break;
                    current  = accepted;
                    progress = true;
                    if (i >= current.Count) break;
                }
                if (i >= current.Count) break;

                for (var j = 0; j < current.Steps[i].Answers.Count && replays < MaxReplays; j++)
                {
                    var answer = current.Steps[i].Answers[j];
                    var rank   = answer.Options.ToList().IndexOf(answer.Answer);
                    for (var simpler = 0; simpler < rank && replays < MaxReplays; simpler++)
                    {
                        var candidate = current.Clone();
                        candidate.Steps[i].Answers[j] = answer with { Answer = answer.Options[simpler] };
                        var accepted = await TryAsync(candidate);
                        if (accepted is null) continue;
                        current  = accepted;
                        progress = true;
                        break;
                    }
                    if (i >= current.Count) break;
                }
                if (i >= current.Count) break;
            }
        }

        var final = await RunExecutor.ExecuteAsync(new ReplaySource(current), options);
        return new ShrinkOutcome(current, final.Story, current.Count, replays, Reproduced: true);
    }
}
