namespace TermPoint.Tests.Harness;

/// <summary>
/// One event that is enabled right now, as a decision source sees it: its stable key ("Edit(bob)") and how much it
/// should be favoured by a random choice.
/// </summary>
/// <param name="Key">The event's key (see <see cref="HarnessEvent.Key"/>).</param>
/// <param name="Weight">Relative weight for a random pick (1 or more).</param>
internal readonly record struct EventOption(string Key, int Weight);

/// <summary>
/// Where the choices of a generated run come from (spec item 9.3, step 9.3c). The run asks at every choice point:
/// <list type="bullet">
/// <item><description>which of the enabled events happens next (or none, which ends the run);</description></item>
/// <item><description>how a dialog is answered (Affirmative / Negative / closed with the X for a choice; a path or Cancel
/// for a picker; completed or cancelled for the stale-lock wait; a database or Exit for the chooser);</description></item>
/// <item><description>after each event, whether the run should go on from the state it reached.</description></item>
/// </list>
/// The parameters of an event (how much time passes) are part of the event's key ("AdvanceTime(4m)"), so they are
/// chosen with the event. Every run is recorded (<see cref="RunRecord"/>) and can be replayed exactly with a
/// <see cref="ReplaySource"/>.
/// </summary>
internal interface IDecisionSource
{
    /// <summary>Chooses which enabled event happens next.</summary>
    /// <param name="enabled">The events that are enabled now, in a fixed order; never empty.</param>
    /// <param name="eventsSoFar">How many events have already happened in this run.</param>
    /// <returns>The chosen event's key, or null to end the run.</returns>
    string? NextEvent(IReadOnlyList<EventOption> enabled, int eventsSoFar);

    /// <summary>Chooses how a dialog is answered.</summary>
    /// <param name="dialog">Which dialog it is ("bob: Restore Unsaved Changes?"); for the record and for replay matching.</param>
    /// <param name="options">The possible answers, simplest first.</param>
    /// <returns>One of <paramref name="options"/>.</returns>
    string Answer(string dialog, IReadOnlyList<string> options);

    /// <summary>Called after each event with a key describing the abstract state the run is in.</summary>
    /// <param name="stateKey">Computes the abstract state (see <see cref="AbstractState"/>); a source that does not need it never calls it, because computing it reads several files.</param>
    /// <param name="eventsSoFar">How many events have happened, including the one that just ended.</param>
    /// <returns>False to stop the run here (this state was already explored at least as deeply); true to go on.</returns>
    bool AfterEvent(Func<string> stateKey, int eventsSoFar);
}

/// <summary>One dialog answer inside a recorded event.</summary>
/// <param name="Dialog">Which dialog it was.</param>
/// <param name="Answer">The answer given.</param>
/// <param name="Options">All the answers that were possible, simplest first (so a shrinker can try a simpler one).</param>
internal sealed record RecordedAnswer(string Dialog, string Answer, IReadOnlyList<string> Options);

/// <summary>One recorded event with the dialog answers given inside it.</summary>
internal sealed class RecordedStep
{
    /// <summary>The event's key.</summary>
    public required string EventKey { get; init; }

    /// <summary>The answers given to the dialogs this event raised, in order.</summary>
    public List<RecordedAnswer> Answers { get; init; } = new();

    /// <summary>A deep copy.</summary>
    public RecordedStep Clone() => new() { EventKey = EventKey, Answers = Answers.ToList() };
}

/// <summary>
/// The decisions of one run, recorded as they were made: events by key, each with the dialog answers given inside it.
/// Keys, not positions, identify events, so a record stays meaningful when the shrinker removes an event before it.
/// </summary>
internal sealed class RunRecord
{
    /// <summary>The events in order.</summary>
    public List<RecordedStep> Steps { get; init; } = new();

    /// <summary>The number of events.</summary>
    public int Count => Steps.Count;

    /// <summary>A deep copy.</summary>
    public RunRecord Clone() => new() { Steps = Steps.Select(s => s.Clone()).ToList() };

    /// <summary>The first <paramref name="count"/> events, as a new record.</summary>
    /// <param name="count">How many events to keep.</param>
    public RunRecord Take(int count) => new() { Steps = Steps.Take(count).Select(s => s.Clone()).ToList() };

    /// <summary>The record without the events at <paramref name="start"/> up to <paramref name="start"/> + <paramref name="length"/>.</summary>
    /// <param name="start">Index of the first event to drop.</param>
    /// <param name="length">How many events to drop.</param>
    public RunRecord Without(int start, int length)
        => new() { Steps = Steps.Where((_, i) => i < start || i >= start + length).Select(s => s.Clone()).ToList() };

    /// <summary>The record as lines, one per event ("Open(bob)  [Restore Unsaved Changes?=Negative]"), for the report.</summary>
    public IEnumerable<string> ToLines()
        => Steps.Select((s, i) => $"{i + 1,3}. {s.EventKey}" +
                                  (s.Answers.Count == 0 ? "" : "  [" + string.Join("; ", s.Answers.Select(a => $"{a.Dialog} = {a.Answer}")) + "]"));
}

/// <summary>
/// Wraps another source and records every decision it makes into a <see cref="RunRecord"/>. The executor always runs
/// through one, so every run, whatever its source, can be replayed.
/// </summary>
internal sealed class RecordingSource : IDecisionSource
{
    /// <summary>The source that really decides.</summary>
    private readonly IDecisionSource _inner;

    /// <summary>The event whose dialogs are being recorded, or null before the first event.</summary>
    private RecordedStep? _current;

    /// <summary>Creates the recorder.</summary>
    /// <param name="inner">The source that decides.</param>
    public RecordingSource(IDecisionSource inner)
    {
        _inner = inner;
        Record = new RunRecord();
    }

    /// <summary>What has been decided so far.</summary>
    public RunRecord Record { get; }

    /// <inheritdoc/>
    public string? NextEvent(IReadOnlyList<EventOption> enabled, int eventsSoFar)
    {
        var key = _inner.NextEvent(enabled, eventsSoFar);
        if (key is null) return null;

        _current = new RecordedStep { EventKey = key };
        Record.Steps.Add(_current);
        return key;
    }

    /// <inheritdoc/>
    public string Answer(string dialog, IReadOnlyList<string> options)
    {
        var answer = _inner.Answer(dialog, options);
        _current?.Answers.Add(new RecordedAnswer(dialog, answer, options.ToArray()));
        return answer;
    }

    /// <inheritdoc/>
    public bool AfterEvent(Func<string> stateKey, int eventsSoFar) => _inner.AfterEvent(stateKey, eventsSoFar);
}

/// <summary>
/// Replays a recorded run exactly. An event whose key is not enabled when its turn comes is skipped (a run the shrinker
/// has changed may no longer reach it); a dialog answer is taken from the record when it names the same dialog and the
/// answer is still possible, and is otherwise the simplest one. The run ends when the record does.
/// </summary>
internal sealed class ReplaySource : IDecisionSource
{
    /// <summary>The events to replay.</summary>
    private readonly RunRecord _record;

    /// <summary>Index of the next event to try.</summary>
    private int _next;

    /// <summary>The event being replayed, or null.</summary>
    private RecordedStep? _current;

    /// <summary>Index of the next unused answer of <see cref="_current"/>.</summary>
    private int _answer;

    /// <summary>Creates the source.</summary>
    /// <param name="record">The decisions to replay.</param>
    public ReplaySource(RunRecord record) => _record = record;

    /// <inheritdoc/>
    public string? NextEvent(IReadOnlyList<EventOption> enabled, int eventsSoFar)
    {
        while (_next < _record.Steps.Count)
        {
            var step = _record.Steps[_next++];
            if (enabled.Any(e => e.Key == step.EventKey))
            {
                _current = step;
                _answer  = 0;
                return step.EventKey;
            }
        }
        return null;
    }

    /// <inheritdoc/>
    public string Answer(string dialog, IReadOnlyList<string> options)
    {
        if (_current is not null && _answer < _current.Answers.Count)
        {
            var recorded = _current.Answers[_answer];
            if (recorded.Dialog == dialog)
            {
                _answer++;
                if (options.Contains(recorded.Answer)) return recorded.Answer;
            }
        }
        return options[0];
    }

    /// <inheritdoc/>
    public bool AfterEvent(Func<string> stateKey, int eventsSoFar) => true;
}

/// <summary>
/// Random decisions from a seed: the same seed always gives the same run. Events are picked with their weights; dialog
/// answers uniformly. The run ends after a fixed number of events.
/// </summary>
internal sealed class RandomSource : IDecisionSource
{
    /// <summary>The generator, seeded.</summary>
    private readonly Random _random;

    /// <summary>The number of events after which the run ends.</summary>
    private readonly int _maxEvents;

    /// <summary>Creates the source.</summary>
    /// <param name="seed">The seed.</param>
    /// <param name="maxEvents">How many events the run has.</param>
    public RandomSource(int seed, int maxEvents)
    {
        _random    = new Random(seed);
        _maxEvents = maxEvents;
    }

    /// <inheritdoc/>
    public string? NextEvent(IReadOnlyList<EventOption> enabled, int eventsSoFar)
    {
        if (eventsSoFar >= _maxEvents) return null;

        var total = enabled.Sum(e => e.Weight);
        var pick  = _random.Next(total);
        foreach (var option in enabled)
        {
            if (pick < option.Weight) return option.Key;
            pick -= option.Weight;
        }
        return enabled[^1].Key;
    }

    /// <inheritdoc/>
    public string Answer(string dialog, IReadOnlyList<string> options) => options[_random.Next(options.Count)];

    /// <inheritdoc/>
    public bool AfterEvent(Func<string> stateKey, int eventsSoFar) => true;
}

/// <summary>
/// The path through the tree of decisions that the exhaustive search is on: the choice made at each choice point so far,
/// and how many options there were. Backtracking moves to the next unexplored option of the deepest choice point.
/// </summary>
internal sealed class ChoicePath
{
    /// <summary>The choices so far, from the first choice point of the run to the last.</summary>
    public List<(int Index, int Count)> Choices { get; } = new();

    /// <summary>
    /// Moves to the next unexplored path: drops the trailing choice points that have no option left, then takes the next
    /// option of the last remaining one.
    /// </summary>
    /// <returns>False when every path has been explored.</returns>
    public bool Advance()
    {
        while (Choices.Count > 0)
        {
            var (index, count) = Choices[^1];
            if (index + 1 < count)
            {
                Choices[^1] = (index + 1, count);
                return true;
            }
            Choices.RemoveAt(Choices.Count - 1);
        }
        return false;
    }
}

/// <summary>
/// One run of the bounded exhaustive search: follows the <see cref="ChoicePath"/> as far as it goes (replaying the
/// prefix exactly, because runs are deterministic), then takes the first option at each new choice point and extends the
/// path. Dialog answers are choice points like events but do not count towards the depth. After each event the abstract
/// state is looked up in the shared memo: a state already explored at an equal or smaller depth ends the run, because
/// everything below it has been (or is being) explored.
/// </summary>
internal sealed class ExhaustiveSource : IDecisionSource
{
    /// <summary>The path being explored (extended by this run).</summary>
    private readonly ChoicePath _path;

    /// <summary>The deepest number of events a run may have.</summary>
    private readonly int _depth;

    /// <summary>Abstract state to the smallest depth at which it has been reached; shared by all runs of the search.</summary>
    private readonly Dictionary<string, int> _memo;

    /// <summary>Index of the next choice point of this run.</summary>
    private int _position;

    /// <summary>The number of choice points the path had when this run began: the prefix this run replays. An event that ends before the last of them is consumed is a pure replay of something already explored, and must not be cut short by the memo it left behind.</summary>
    private readonly int _prefixLength;

    /// <summary>Creates the source for one run.</summary>
    /// <param name="path">The path to follow and extend.</param>
    /// <param name="depth">The maximum number of events.</param>
    /// <param name="memo">The shared memo of explored states.</param>
    public ExhaustiveSource(ChoicePath path, int depth, Dictionary<string, int> memo)
    {
        _path  = path;
        _depth = depth;
        _memo  = memo;
        _prefixLength = path.Choices.Count;
    }

    /// <summary>True when the run ended because its state was already explored.</summary>
    public bool EndedByMemo { get; private set; }

    /// <summary>
    /// Takes the choice at the next choice point: the recorded one while replaying the prefix, option 0 at a new one.
    /// </summary>
    /// <param name="count">The number of options at this choice point.</param>
    /// <returns>The index of the option taken.</returns>
    /// <exception cref="InvalidOperationException">The replayed prefix met a different number of options than before: the run is not deterministic.</exception>
    private int Choose(int count)
    {
        if (_position < _path.Choices.Count)
        {
            var (index, recordedCount) = _path.Choices[_position++];
            if (recordedCount != count)
                throw new InvalidOperationException(
                    $"The exhaustive search is not deterministic: choice point {_position} had {recordedCount} options the first time and {count} now.");
            return index;
        }

        _path.Choices.Add((0, count));
        _position++;
        return 0;
    }

    /// <inheritdoc/>
    public string? NextEvent(IReadOnlyList<EventOption> enabled, int eventsSoFar)
        => eventsSoFar >= _depth ? null : enabled[Choose(enabled.Count)].Key;

    /// <inheritdoc/>
    public string Answer(string dialog, IReadOnlyList<string> options) => options[Choose(options.Count)];

    /// <inheritdoc/>
    public bool AfterEvent(Func<string> stateKey, int eventsSoFar)
    {
        // An event inside the replayed prefix was explored (and its state remembered) by the run that made the prefix.
        if (_position < _prefixLength) return true;

        var key = stateKey();
        if (eventsSoFar > 0 && _memo.TryGetValue(key, out var seenAt) && seenAt <= eventsSoFar)
        {
            EndedByMemo = true;
            return false;
        }
        if (!_memo.TryGetValue(key, out var smallest) || eventsSoFar < smallest)
            _memo[key] = eventsSoFar;
        return true;
    }
}
