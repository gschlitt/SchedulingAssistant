namespace TermPoint.Tests.Harness;

/// <summary>
/// The one event that happens while an operation is held (spec item 9.3, step 9.3c-2b). It may be a fixed short sequence of
/// existing catalogue events ("Alice takes over" is enough fake time for Bob's lock to look stale, then Alice opens the database and
/// takes it over), and it still counts as one key and one event. "Bob" and "Alice" are written generically: the paused computer and
/// another computer, so the same actions serve every pause.
///
/// <para>An action is only <see cref="IsPossible"/> in some states ("Bob edits" needs Bob's connection to be open), and the question is
/// asked at the very moment of the pause, so only events that are possible THEN are offered or run.</para>
/// </summary>
internal abstract class PauseAction
{
    /// <summary>The action's key as it is written inside a <see cref="PauseEvent"/> key: letters only ("AliceTakesOverAndSaves").</summary>
    public abstract string Key { get; }

    /// <summary>The action as a phrase that completes "... it is held while ..." ("Alice takes over the lock and saves").</summary>
    public abstract string Story { get; }

    /// <summary>True when the action makes sense in the state the world is in right now.</summary>
    /// <param name="ctx">The run.</param>
    public abstract bool IsPossible(RunContext ctx);

    /// <summary>
    /// True when the action is KNOWN to have to wait for the held operation in the state the world is in right now: it asks the
    /// operation's own computer to do something that waits for the operation to finish (the switch's release waits for a save in
    /// flight). Designed behaviour, not a violation; it costs the whole grace period to demonstrate, so the default sweep leaves such
    /// combinations to width 2 and a proof test pins the behaviour.
    /// </summary>
    /// <param name="ctx">The run.</param>
    public virtual bool WaitsForTheHeldOperation(RunContext ctx) => false;

    /// <summary>
    /// True when the action may be offered while <paramref name="held"/> is the operation that is held. The default is yes: nearly every
    /// action makes sense whatever is held. An action overrides it to leave out a pairing the harness deliberately does not pursue (see
    /// <see cref="PausedComputerSwitchesAction"/>). Both the pause sweep (when it plans its cases) and <see cref="PauseEvent.IsEnabled"/> ask it,
    /// so a pairing that is left out is neither planned nor run, at any width, and a recorded key that names it is skipped on replay.
    /// </summary>
    /// <param name="held">The event whose operation is held (the pause's inner event).</param>
    public virtual bool IsOfferedWhileHolding(ComputerEvent held) => true;

    /// <summary>Carries the action out (through the world's funnel, so the story and the promise checks apply as always).</summary>
    /// <param name="ctx">The run.</param>
    /// <returns>A task that completes when the action is done.</returns>
    public abstract Task RunAsync(RunContext ctx);

    /// <summary>The computers the action works on: the ones the harness abandons if the action hangs.</summary>
    /// <param name="ctx">The run.</param>
    public virtual IReadOnlyList<Computer> ComputersOf(RunContext ctx) => Array.Empty<Computer>();

    /// <summary>A name with a capital letter ("bob" to "Bob").</summary>
    /// <param name="name">A computer's name.</param>
    protected static string Capital(string name) => char.ToUpperInvariant(name[0]) + name[1..];
}

/// <summary>"Alice takes over": time passes for the paused computer's lock to look stale, then the other computer opens the database and takes it over.</summary>
internal sealed class TakesOverAction : PauseAction
{
    /// <summary>The computer that takes over ("alice").</summary>
    private readonly string _other;

    /// <summary>True when the other computer also edits and saves once it has taken over.</summary>
    private readonly bool _andSaves;

    /// <summary>Creates the action.</summary>
    /// <param name="other">The computer that takes over (a computer other than the paused one).</param>
    /// <param name="andSaves">True for "takes over and saves": it then makes an edit and presses Save.</param>
    public TakesOverAction(string other, bool andSaves)
    {
        _other    = other;
        _andSaves = andSaves;
    }

    /// <inheritdoc/>
    public override string Key => $"{Capital(_other)}TakesOver{(_andSaves ? "AndSaves" : "")}";

    /// <inheritdoc/>
    public override string Story
        => $"{Capital(_other)} takes over the lock{(_andSaves ? ", edits and saves" : "")}";

    /// <inheritdoc/>
    public override bool IsPossible(RunContext ctx) => !ctx.Computer(_other).HasOpenSession;

    /// <inheritdoc/>
    public override IReadOnlyList<Computer> ComputersOf(RunContext ctx) => new[] { ctx.Computer(_other) };

    /// <inheritdoc/>
    public override async Task RunAsync(RunContext ctx)
    {
        ctx.World.AdvanceTime(TimeSpan.FromMinutes(4));      // the paused computer's heartbeat is now stale
        var other = ctx.Computer(_other);
        await new OpenEvent(_other).ExecuteAsync(ctx);       // the simplest answers: take over, wait to completion

        if (!_andSaves) return;
        if (other.HasLiveSession) other.Edit();
        if (other.HasOpenSession) await other.SaveAsync();
    }
}

/// <summary>"IT restores an old copy": the snapshot the run holds is copied back over the shared database.</summary>
internal sealed class RestoresOldFileAction : PauseAction
{
    /// <inheritdoc/>
    public override string Key => "ItRestoresOldFile";

    /// <inheritdoc/>
    public override string Story => "IT copies an older copy back over the shared database";

    /// <inheritdoc/>
    public override bool IsPossible(RunContext ctx) => ctx.Snapshot is not null;

    /// <inheritdoc/>
    public override Task RunAsync(RunContext ctx) => new ItRestoresOldFileEvent().ExecuteAsync(ctx);
}

/// <summary>"The lock file is deleted": something removes D's lock file from the share.</summary>
internal sealed class DeletesLockFileAction : PauseAction
{
    /// <inheritdoc/>
    public override string Key => "TheLockFileIsDeleted";

    /// <inheritdoc/>
    public override string Story => "somebody deletes the lock file from the share";

    /// <inheritdoc/>
    public override bool IsPossible(RunContext ctx) => ctx.World.LockFileExists;

    /// <inheritdoc/>
    public override Task RunAsync(RunContext ctx) => new DeleteLockFileEvent().ExecuteAsync(ctx);
}

/// <summary>"Time passes": the shared fake clock moves on (four minutes: long enough for a heartbeat to go stale).</summary>
internal sealed class TimePassesAction : PauseAction
{
    /// <inheritdoc/>
    public override string Key => "TimePasses";

    /// <inheritdoc/>
    public override string Story => "four minutes pass";

    /// <inheritdoc/>
    public override bool IsPossible(RunContext ctx) => true;

    /// <inheritdoc/>
    public override Task RunAsync(RunContext ctx)
    {
        ctx.World.AdvanceTime(TimeSpan.FromMinutes(4));
        return Task.CompletedTask;
    }
}

/// <summary>"Bob edits": the paused computer's own edit while its operation is held (rule 3, background work during a save).</summary>
internal sealed class PausedComputerEditsAction : PauseAction
{
    /// <summary>The paused computer ("bob").</summary>
    private readonly string _paused;

    /// <summary>Creates the action.</summary>
    /// <param name="paused">The paused computer.</param>
    public PausedComputerEditsAction(string paused) => _paused = paused;

    /// <inheritdoc/>
    public override string Key => $"{Capital(_paused)}Edits";

    /// <inheritdoc/>
    public override string Story => $"{Capital(_paused)} makes an edit";

    /// <inheritdoc/>
    public override bool IsPossible(RunContext ctx) => ctx.Computer(_paused).HasLiveSession;

    /// <inheritdoc/>
    public override Task RunAsync(RunContext ctx) => new EditEvent(_paused).ExecuteAsync(ctx);

    /// <inheritdoc/>
    public override IReadOnlyList<Computer> ComputersOf(RunContext ctx) => new[] { ctx.Computer(_paused) };
}

/// <summary>"Bob switches": the paused computer's person switches database while its operation is held (to the second database, or back to the first).</summary>
internal sealed class PausedComputerSwitchesAction : PauseAction
{
    /// <summary>The paused computer ("bob").</summary>
    private readonly string _paused;

    /// <summary>Creates the action.</summary>
    /// <param name="paused">The paused computer.</param>
    public PausedComputerSwitchesAction(string paused) => _paused = paused;

    /// <inheritdoc/>
    public override string Key => $"{Capital(_paused)}Switches";

    /// <inheritdoc/>
    public override string Story => $"{Capital(_paused)} switches database";

    /// <inheritdoc/>
    public override bool IsPossible(RunContext ctx) => ctx.Computer(_paused).HasOpenSession;

    /// <summary>
    /// Not offered while the same computer's CLOSE is held. <b>Not pursued, by the user's decision of 2026-10-21</b> ("pretty abstruse"): in the
    /// pairing the harness found at width 2, Bob has an edit, a 1.2.2 colleague changes the database, Bob closes (the final save is refused,
    /// so the close keeps his working copy), and a SWITCH by the same person runs while the close is held after its release
    /// (<c>Pause(Close(bob) at Close.AfterRelease; BobSwitches)</c>, 4 events). The switch starts from a session the close has already
    /// released, asks <c>ReleaseAsync</c> for no save, is told Success, and <c>SessionLifecycle.SwitchCoreAsync</c> deletes the working copy it
    /// believes is in the database: bob-1 ends up nowhere. It needs a person to start a switch while the window is closing, which the user judged
    /// too unlikely to need a finding of its own, so it is not in section 3 of the design note. The pairing is left out at every width instead of
    /// being excused in a test, so that any OTHER unexplained breach fails the run again. Another computer's events during Bob's close stay.
    /// Reverse this (delete the override) if the decision is ever reversed.
    /// </summary>
    /// <param name="held">The event whose operation is held.</param>
    public override bool IsOfferedWhileHolding(ComputerEvent held)
        => !(held is CloseEvent && string.Equals(held.ComputerName, _paused, StringComparison.Ordinal));

    /// <summary>A switch leaves the old database through <c>CheckoutService.ReleaseAsync</c>, which waits (up to a minute) for a save in flight, so it waits for the held save.</summary>
    /// <param name="ctx">The run.</param>
    public override bool WaitsForTheHeldOperation(RunContext ctx) => ctx.Computer(_paused).SaveInFlight;

    /// <inheritdoc/>
    public override IReadOnlyList<Computer> ComputersOf(RunContext ctx) => new[] { ctx.Computer(_paused) };

    /// <inheritdoc/>
    public override Task RunAsync(RunContext ctx)
        => ctx.Computer(_paused).SessionIsOnSharedDatabase
            ? new SwitchAwayEvent(_paused).ExecuteAsync(ctx)
            : new SwitchBackEvent(_paused).ExecuteAsync(ctx);
}

/// <summary>The catalogue of the events that can happen while an operation is held.</summary>
internal static class PauseActions
{
    /// <summary>Every action for every computer, in a fixed order (what a pause key is parsed against).</summary>
    public static IReadOnlyList<PauseAction> All { get; } = EventCatalogue.ComputerNames.SelectMany(For).DistinctBy(a => a.Key).ToList();

    /// <summary>
    /// The actions that make sense while <paramref name="paused"/>'s operation is held: the other computer takes over, takes over
    /// and saves; IT restores an old copy; the lock file is deleted; time passes; the paused computer edits; the paused computer switches.
    /// </summary>
    /// <param name="paused">The paused computer's name.</param>
    /// <returns>The actions, in a fixed order.</returns>
    public static IReadOnlyList<PauseAction> For(string paused)
    {
        var other = EventCatalogue.ComputerNames.First(n => n != paused);
        return new PauseAction[]
        {
            new TakesOverAction(other, andSaves: false),
            new TakesOverAction(other, andSaves: true),
            new RestoresOldFileAction(),
            new DeletesLockFileAction(),
            new TimePassesAction(),
            new PausedComputerEditsAction(paused),
            new PausedComputerSwitchesAction(paused)
        };
    }
}
