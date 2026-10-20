using System.Diagnostics;

namespace TermPoint.Tests.Harness;

/// <summary>One breach found during a generated run, with where in the run it appeared.</summary>
/// <param name="Signature">Identity for shrinking and counting: invariant, kind and finding, or the exception for a crash of the harness itself.</param>
/// <param name="FindingId">"F1", "F5", ... or "new".</param>
/// <param name="Invariant">"I1" to "I5", or "exception".</param>
/// <param name="Reason">One line saying what is wrong.</param>
/// <param name="EventIndex">The number of events that had happened when it was found (the last of them caused it); 0 when found before any event.</param>
internal sealed record FoundViolation(string Signature, string FindingId, string Invariant, string Reason, int EventIndex)
{
    /// <summary>True when the finding is one expected on today's code (<see cref="KnownFindings"/>).</summary>
    public bool IsKnown => KnownFindings.IsKnown(FindingId);
}

/// <summary>What one generated run did and found.</summary>
internal sealed class RunResult
{
    /// <summary>The decisions the run made, in order (only the events that really happened).</summary>
    public required RunRecord Record { get; init; }

    /// <summary>Every breach found, known or new, in the order found.</summary>
    public required IReadOnlyList<FoundViolation> Violations { get; init; }

    /// <summary>The number of events that happened.</summary>
    public required int Events { get; init; }

    /// <summary>True when the run stopped because its state had already been explored (the exhaustive search's memo).</summary>
    public required bool EndedByMemo { get; init; }

    /// <summary>The keys of every event that was enabled at some point of the run.</summary>
    public required IReadOnlySet<string> EnabledKeys { get; init; }

    /// <summary>The printed story of the run, with its decisions.</summary>
    public required string Story { get; init; }

    /// <summary>How long the run took.</summary>
    public required TimeSpan Elapsed { get; init; }

    /// <summary>True when an event did not finish in time (the run was abandoned; the process may still have work running).</summary>
    public bool Hung { get; init; }

    /// <summary>
    /// For each event that happened (in order), the crash points it reached, with the name of the computer that reached
    /// each ("?" when none could be told). The crash sweep's dry run reads the last event's.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<(string Computer, string Point)>> HitsByEvent { get; init; }
        = Array.Empty<IReadOnlyList<(string, string)>>();

    /// <summary>The power cuts that fired in the run ("bob@Save.AfterRename"), in order.</summary>
    public IReadOnlyList<string> FiredCuts { get; init; } = Array.Empty<string>();

    /// <summary>The breaches that no known finding explains: these fail a generated test.</summary>
    public IEnumerable<FoundViolation> NewViolations => Violations.Where(v => !v.IsKnown);
}

/// <summary>What a generated run needs besides its decision source.</summary>
internal sealed class RunOptions
{
    /// <summary>What the story is of.</summary>
    public string Title { get; init; } = "a generated run";

    /// <summary>Runs on every new world before the first event (the planted-bug test installs its hook here); null for none.</summary>
    public Action<World>? ConfigureWorld { get; init; }

    /// <summary>
    /// Events the run knows besides <see cref="EventCatalogue.Build"/>: the crash sweep's plain events and its power-cut
    /// events (<see cref="EventCatalogue.ExtrasFor"/> builds the list for the keys a run replays). Empty for a search.
    /// </summary>
    public IReadOnlyList<HarnessEvent> ExtraEvents { get; init; } = Array.Empty<HarnessEvent>();

    /// <summary>A copy of the options with another title: what a shrunk run is called, so its story does not carry the name of the longer run it came from.</summary>
    /// <param name="title">The new title.</param>
    public RunOptions WithTitle(string title)
        => new() { Title = title, ConfigureWorld = ConfigureWorld, ExtraEvents = ExtraEvents, EventTimeout = EventTimeout };

    /// <summary>How long one event may take before the run is called hung.</summary>
    public TimeSpan EventTimeout { get; init; } = TimeSpan.FromSeconds(90);
}

/// <summary>
/// Executes one generated run (spec item 9.3, step 9.3c): builds a fresh <see cref="World"/> with two computers, then
/// repeatedly asks the decision source which enabled event happens next, executes it through the World's funnel (so the
/// story and every invariant apply exactly as in a hand-written walk), and stops when the source says so, when the
/// state was already explored, or when something new breaks.
///
/// <para>Violations of the known findings are recorded and the run goes on. A violation no known finding explains, or an
/// exception thrown by an event, ends the run: that is a failure, reported with its story.</para>
/// </summary>
internal static class RunExecutor
{
    /// <summary>Runs one generated run.</summary>
    /// <param name="source">Where the decisions come from.</param>
    /// <param name="options">The run's options.</param>
    /// <returns>What happened.</returns>
    public static async Task<RunResult> ExecuteAsync(IDecisionSource source, RunOptions options)
    {
        var stopwatch = Stopwatch.StartNew();
        var recording = new RecordingSource(source);
        var catalogue = EventCatalogue.Build().Concat(options.ExtraEvents).ToList();
        var hitsByEvent = new List<IReadOnlyList<(string Computer, string Point)>>();
        var enabledEver = new HashSet<string>();
        var found = new List<FoundViolation>();
        var seenViolations = 0;
        var events = 0;
        var endedByMemo = false;
        var hung = false;

        using var world = new World(null, options.Title);
        var ctx = new RunContext(world, recording);
        world.ExpectFindings(KnownFindings.Ids.ToArray());
        world.PromptPolicy = GeneratedPrompts.Create(ctx);
        world.CreateDatabaseFromTemplate();
        foreach (var name in EventCatalogue.ComputerNames) world.AddComputer(name);
        options.ConfigureWorld?.Invoke(world);

        // The starting state counts as explored at depth 0, so an event that changes nothing is not explored again.
        recording.AfterEvent(() => AbstractState.Key(ctx), 0);

        while (true)
        {
            var enabled = catalogue.Where(e => e.IsEnabled(ctx)).ToList();
            foreach (var e in enabled) enabledEver.Add(e.Key);
            if (enabled.Count == 0) break;

            var key = recording.NextEvent(enabled.Select(e => new EventOption(e.Key, e.Weight)).ToList(), events);
            if (key is null) break;

            var chosen = enabled.First(e => e.Key == key);
            ctx.BeginEvent();
            events++;
            var hitsBefore = world.HitCount;

            string? crash = null;
            var crashType = "exception";
            try
            {
                var task = chosen.ExecuteAsync(ctx);
                var finished = await Task.WhenAny(task, Task.Delay(options.EventTimeout));
                if (finished != task)
                {
                    hung = true;
                    crashType = "hang";
                    crash = $"event {key} did not finish within {options.EventTimeout.TotalSeconds:0} seconds";
                }
                else
                {
                    await task;
                }
            }
            catch (HarnessViolationException)
            {
                // A violation no known finding explains. It is already recorded in the world; collected below.
            }
            catch (Exception ex)
            {
                crashType = ex.GetType().Name;
                crash = $"event {key} threw {ex.GetType().Name}: {FirstLine(ex.Message)}";
            }

            hitsByEvent.Add(world.HitsSince(hitsBefore));

            // Collect the violations this event produced.
            var all = world.Violations;
            for (; seenViolations < all.Count; seenViolations++)
            {
                var v = all[seenViolations];
                found.Add(new FoundViolation($"{v.Invariant}/{v.Kind}/{v.FindingId}", v.FindingId, v.Invariant, v.Reason, events));
            }

            if (crash is not null)
            {
                var parenAt = key.IndexOf('(');
                var kind = parenAt < 0 ? key : key[..parenAt];
                found.Add(new FoundViolation($"exception/{crashType}/{kind}", Findings.NewFinding, "exception", crash, events));
                break;
            }
            if (found.Any(v => !v.IsKnown)) break;

            if (!recording.AfterEvent(() => AbstractState.Key(ctx), events))
            {
                endedByMemo = true;
                break;
            }
        }

        var notes = new List<string> { "", "DECISIONS (replayable)" };
        notes.AddRange(recording.Record.ToLines().Select(l => "  " + l));
        var story = world.PrintStory(notes);

        stopwatch.Stop();
        return new RunResult
        {
            Record      = recording.Record,
            Violations  = found,
            Events      = events,
            EndedByMemo = endedByMemo,
            EnabledKeys = enabledEver,
            Story       = story,
            Elapsed     = stopwatch.Elapsed,
            Hung        = hung,
            HitsByEvent = hitsByEvent,
            FiredCuts   = world.FiredCuts
        };
    }

    /// <summary>The first line of a text, shortened.</summary>
    /// <param name="text">The text.</param>
    private static string FirstLine(string text)
    {
        var line = text.Split('\n')[0].Trim();
        return line.Length <= 160 ? line : line[..160] + "...";
    }
}

/// <summary>The size of a generated exploration; defaults chosen so the whole generated suite takes about ten minutes, overridable by environment variables.</summary>
/// <param name="Depth">The bounded exhaustive search goes this many events deep.</param>
/// <param name="Seeds">The number of random walks.</param>
/// <param name="Steps">The number of events in each random walk.</param>
/// <param name="CrashWidth">How wide the crash sweep goes (step 9.3c-2a): 1 is the fixed small set of starting situations and the first time each point is reached; 2 adds more starting situations, later times a point is reached and a follow-up after the restart.</param>
internal sealed record HarnessBudget(int Depth, int Seeds, int Steps, int CrashWidth = HarnessBudget.DefaultCrashWidth)
{
    /// <summary>Environment variable: depth of the exhaustive search.</summary>
    public const string DepthVariable = "TERMPOINT_HARNESS_DEPTH";

    /// <summary>Environment variable: number of random walks.</summary>
    public const string SeedsVariable = "TERMPOINT_HARNESS_SEEDS";

    /// <summary>Environment variable: events per random walk.</summary>
    public const string StepsVariable = "TERMPOINT_HARNESS_STEPS";

    /// <summary>The default depth.</summary>
    public const int DefaultDepth = 3;

    /// <summary>The default number of random walks.</summary>
    public const int DefaultSeeds = 300;

    /// <summary>The default number of events per random walk.</summary>
    public const int DefaultSteps = 30;

    /// <summary>Environment variable: width of the crash sweep (1 or 2).</summary>
    public const string CrashWidthVariable = "TERMPOINT_HARNESS_CRASH_WIDTH";

    /// <summary>The default width of the crash sweep.</summary>
    public const int DefaultCrashWidth = 1;

    /// <summary>Reads the budget from the environment, with the defaults for anything not set (or not a positive number).</summary>
    public static HarnessBudget FromEnvironment()
        => new(Read(DepthVariable, DefaultDepth), Read(SeedsVariable, DefaultSeeds), Read(StepsVariable, DefaultSteps),
               Math.Min(2, Read(CrashWidthVariable, DefaultCrashWidth)));

    /// <summary>Reads one positive integer from an environment variable.</summary>
    /// <param name="name">The variable.</param>
    /// <param name="fallback">The value when it is missing or invalid.</param>
    private static int Read(string name, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0 ? value : fallback;
}

