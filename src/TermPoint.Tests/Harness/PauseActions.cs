using TermPoint.Services;

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

    /// <summary>
    /// True when the action may be offered while <paramref name="held"/> is held at the crash point <paramref name="point"/>. The default is yes. An
    /// action overrides it to be offered at one point only (see <see cref="PausedComputerNetworkDropsAction"/>). It is asked together with
    /// <see cref="IsOfferedWhileHolding"/> by the sweep's plan and by <see cref="PauseEvent.IsEnabled"/>.
    /// </summary>
    /// <param name="held">The event whose operation is held.</param>
    /// <param name="point">The crash point it is held at.</param>
    public virtual bool IsOfferedAtPoint(ComputerEvent held, string point) => true;

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

/// <summary>
/// "Bob's heartbeat ticks": the paused computer's lock-heartbeat timer fires while its operation is held. Offered only while the same
/// computer's SLEEP is held (its wake check held at <c>WakeCheck.AfterVerify</c>): on a real wake the wake check and the heartbeat run at
/// the same moment, which the sleep event itself does one after the other (field bug A12: both found the lock gone and both raised
/// WriteLockLost). Possible only while the computer has a write session, because only a writer's heartbeat verifies the lock.
/// </summary>
internal sealed class PausedComputerHeartbeatTicksAction : PauseAction
{
    /// <summary>The paused computer ("bob").</summary>
    private readonly string _paused;

    /// <summary>Creates the action.</summary>
    /// <param name="paused">The paused computer.</param>
    public PausedComputerHeartbeatTicksAction(string paused) => _paused = paused;

    /// <inheritdoc/>
    public override string Key => $"{Capital(_paused)}sHeartbeatTicks";

    /// <inheritdoc/>
    public override string Story => $"{Capital(_paused)}'s heartbeat ticks";

    /// <inheritdoc/>
    public override bool IsPossible(RunContext ctx)
    {
        var computer = ctx.Computer(_paused);
        return computer.HasOpenSession && computer.Checkout.Mode == CheckoutMode.WriteAccess;
    }

    /// <summary>Offered only while the same computer's sleep is held (the wake check and the heartbeat of a real wake).</summary>
    /// <param name="held">The event whose operation is held.</param>
    public override bool IsOfferedWhileHolding(ComputerEvent held)
        => held is SleepEvent && string.Equals(held.ComputerName, _paused, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override Task RunAsync(RunContext ctx) => ctx.Computer(_paused).HeartbeatTickAsync();

    /// <inheritdoc/>
    public override IReadOnlyList<Computer> ComputersOf(RunContext ctx) => new[] { ctx.Computer(_paused) };
}

/// <summary>
/// "Bob's wake check runs": the paused computer's wake-detection timer fires while its operation is held. Offered only while the same
/// computer's HEARTBEAT tick is held (its ownership check held at <c>HeartbeatVerify.AfterVerify</c>): the reverse of
/// <see cref="PausedComputerHeartbeatTicksAction"/>. The wake check notices a wake only when time has passed since its last tick, so
/// the run must have advanced the clock (a takeover does); otherwise it finds nothing and the case is an ordinary one.
/// </summary>
internal sealed class PausedComputerWakeCheckRunsAction : PauseAction
{
    /// <summary>The paused computer ("bob").</summary>
    private readonly string _paused;

    /// <summary>Creates the action.</summary>
    /// <param name="paused">The paused computer.</param>
    public PausedComputerWakeCheckRunsAction(string paused) => _paused = paused;

    /// <inheritdoc/>
    public override string Key => $"{Capital(_paused)}sWakeCheckRuns";

    /// <inheritdoc/>
    public override string Story => $"{Capital(_paused)}'s wake check runs";

    /// <inheritdoc/>
    public override bool IsPossible(RunContext ctx)
    {
        var computer = ctx.Computer(_paused);
        return computer.HasOpenSession && computer.Checkout.Mode == CheckoutMode.WriteAccess;
    }

    /// <summary>Offered only while the same computer's heartbeat tick is held (the heartbeat and the wake check of a real wake).</summary>
    /// <param name="held">The event whose operation is held.</param>
    public override bool IsOfferedWhileHolding(ComputerEvent held)
        => held is HeartbeatTickEvent && string.Equals(held.ComputerName, _paused, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override Task RunAsync(RunContext ctx) => ctx.Computer(_paused).WakeCheckAsync();

    /// <inheritdoc/>
    public override IReadOnlyList<Computer> ComputersOf(RunContext ctx) => new[] { ctx.Computer(_paused) };
}

/// <summary>
/// "Bob's network drops": the paused computer loses its connection to the share while its OPEN is held at <c>Checkout.AfterLiveHolderSeen</c>, the
/// moment before the copy of the shared database to the read-only snapshot. That copy then fails (field bug A13: the person saw "Checkout Failed" and
/// then "Couldn't Open the Database", two messages for one problem). Offered only there: at other points a dropped network is the business of the
/// network events of the catalogue, and the sweep would multiply its cases by every point of every open.
/// </summary>
internal sealed class PausedComputerNetworkDropsAction : PauseAction
{
    /// <summary>The one crash point the action is offered at.</summary>
    public const string OnlyPoint = "Checkout.AfterLiveHolderSeen";

    /// <summary>The paused computer ("bob").</summary>
    private readonly string _paused;

    /// <summary>Creates the action.</summary>
    /// <param name="paused">The paused computer.</param>
    public PausedComputerNetworkDropsAction(string paused) => _paused = paused;

    /// <inheritdoc/>
    public override string Key => $"{Capital(_paused)}sNetworkDrops";

    /// <inheritdoc/>
    public override string Story => $"{Capital(_paused)}'s network drops";

    /// <inheritdoc/>
    public override bool IsPossible(RunContext ctx) => !ctx.Computer(_paused).NetworkIsFaulty;

    /// <summary>Offered only while the same computer's OPEN is held.</summary>
    /// <param name="held">The event whose operation is held.</param>
    public override bool IsOfferedWhileHolding(ComputerEvent held)
        => held is OpenEvent && string.Equals(held.ComputerName, _paused, StringComparison.Ordinal);

    /// <summary>Offered only at <see cref="OnlyPoint"/>.</summary>
    /// <param name="held">The event whose operation is held.</param>
    /// <param name="point">The crash point it is held at.</param>
    public override bool IsOfferedAtPoint(ComputerEvent held, string point)
        => string.Equals(point, OnlyPoint, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override Task RunAsync(RunContext ctx) => new NetworkDownEvent(_paused).ExecuteAsync(ctx);

    /// <inheritdoc/>
    public override IReadOnlyList<Computer> ComputersOf(RunContext ctx) => new[] { ctx.Computer(_paused) };
}

/// <summary>
/// "Bob's network hangs": the same moment as <see cref="PausedComputerNetworkDropsAction"/> (the paused OPEN is held at <c>Checkout.AfterLiveHolderSeen</c>,
/// just before the copy of the shared database to the read-only snapshot) but the share does not fail: the copy BLOCKS, so only the deadline can end it
/// (spec item 8.2; the 1.2.3 field bug A1 was a copy that blocked before its deadline began). The person must see one message for one problem, as with a drop.
/// </summary>
internal sealed class PausedComputerNetworkHangsAction : PauseAction
{
    /// <summary>The paused computer ("bob").</summary>
    private readonly string _paused;

    /// <summary>Creates the action.</summary>
    /// <param name="paused">The paused computer.</param>
    public PausedComputerNetworkHangsAction(string paused) => _paused = paused;

    /// <inheritdoc/>
    public override string Key => $"{Capital(_paused)}sNetworkHangs";

    /// <inheritdoc/>
    public override string Story => $"{Capital(_paused)}'s network hangs";

    /// <inheritdoc/>
    public override bool IsPossible(RunContext ctx) => !ctx.Computer(_paused).NetworkIsFaulty;

    /// <summary>Offered only while the same computer's OPEN is held.</summary>
    /// <param name="held">The event whose operation is held.</param>
    public override bool IsOfferedWhileHolding(ComputerEvent held)
        => held is OpenEvent && string.Equals(held.ComputerName, _paused, StringComparison.Ordinal);

    /// <summary>Offered only at <see cref="PausedComputerNetworkDropsAction.OnlyPoint"/>.</summary>
    /// <param name="held">The event whose operation is held.</param>
    /// <param name="point">The crash point it is held at.</param>
    public override bool IsOfferedAtPoint(ComputerEvent held, string point)
        => string.Equals(point, PausedComputerNetworkDropsAction.OnlyPoint, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override Task RunAsync(RunContext ctx) => new NetworkHangEvent(_paused).ExecuteAsync(ctx);

    /// <inheritdoc/>
    public override IReadOnlyList<Computer> ComputersOf(RunContext ctx) => new[] { ctx.Computer(_paused) };
}

/// <summary>
/// "Another program opens a file": an antivirus scanner, a backup tool or a cloud-sync client opens the shared database, the temporary file the held save
/// uploads, or the lock file, with a chosen sharing mode, while the save is held (spec item 8.3). The handle stays open until the run lets go of it (the
/// <c>OtherProgramReleases</c> event, a power cut, or the end of the run). Offered only while a SAVE is held (a save is what uses all three files), at the
/// points where a save is between its snapshot and its rename; the temporary file is possible only once the save has copied it.
/// </summary>
internal sealed class OtherProgramOpensAction : PauseAction
{
    /// <summary>The save's points this action is offered at: after the snapshot (the temporary file is not there yet), after it is copied, after it is verified.</summary>
    private static readonly string[] Points = { "Save.AfterLocalSnapshot", "Save.AfterTmpCopy", "Save.AfterTmpVerify" };

    /// <summary>Which file it opens.</summary>
    private readonly OtherProgramFile _file;

    /// <summary>What it lets others do with the file.</summary>
    private readonly OtherProgramShare _share;

    /// <summary>Creates the action.</summary>
    /// <param name="file">Which file it opens.</param>
    /// <param name="share">What it lets others do with the file while it has it open.</param>
    public OtherProgramOpensAction(OtherProgramFile file, OtherProgramShare share)
    {
        _file  = file;
        _share = share;
    }

    /// <inheritdoc/>
    public override string Key => $"OtherProgramOpens{Cap(_file.ToString())}{Cap(_share.ToString())}";

    /// <inheritdoc/>
    public override string Story => $"another program opens {OtherProgramNames.Words(_file)}, {OtherProgramNames.Words(_share)}";

    /// <inheritdoc/>
    public override bool IsPossible(RunContext ctx) => ctx.World.OtherProgramCanOpen(_file);

    /// <summary>Offered only while a save (a Save press or an autosave tick) is held.</summary>
    /// <param name="held">The event whose operation is held.</param>
    public override bool IsOfferedWhileHolding(ComputerEvent held) => held is SaveEvent or AutosaveTickEvent;

    /// <summary>Offered only at <see cref="Points"/>.</summary>
    /// <param name="held">The event whose operation is held.</param>
    /// <param name="point">The crash point it is held at.</param>
    public override bool IsOfferedAtPoint(ComputerEvent held, string point) => Points.Contains(point);

    /// <inheritdoc/>
    public override Task RunAsync(RunContext ctx)
    {
        ctx.World.OtherProgramOpens(_file, _share);
        return Task.CompletedTask;
    }

    /// <summary>Upper-cases the first letter ("SaveTemp" stays, "none" would become "None").</summary>
    /// <param name="text">A word.</param>
    private static string Cap(string text) => char.ToUpperInvariant(text[0]) + text[1..];
}

/// <summary>The catalogue of the events that can happen while an operation is held.</summary>
internal static class PauseActions
{
    /// <summary>Every action for every computer, in a fixed order (what a pause key is parsed against).</summary>
    public static IReadOnlyList<PauseAction> All { get; } = EventCatalogue.ComputerNames.SelectMany(For).DistinctBy(a => a.Key).ToList();

    /// <summary>The "another program opens a file" actions: every file with every sharing mode, in a fixed order.</summary>
    private static IEnumerable<PauseAction> OtherProgramActions()
        => Enum.GetValues<OtherProgramFile>().SelectMany(file =>
            Enum.GetValues<OtherProgramShare>().Select(share => (PauseAction)new OtherProgramOpensAction(file, share)));

    /// <summary>
    /// The actions that make sense while <paramref name="paused"/>'s operation is held: the other computer takes over, takes over
    /// and saves; IT restores an old copy; the lock file is deleted; time passes; the paused computer edits; the paused computer switches; the paused computer's heartbeat ticks (while its
    /// sleep is held); the paused computer's wake check runs (while its heartbeat tick is held); the paused computer's network drops (while its open is held just before the read-only copy).
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
            new PausedComputerSwitchesAction(paused),
            new PausedComputerHeartbeatTicksAction(paused),
            new PausedComputerWakeCheckRunsAction(paused),
            new PausedComputerNetworkDropsAction(paused),
            new PausedComputerNetworkHangsAction(paused)
        }.Concat(OtherProgramActions()).ToArray();
    }
}
