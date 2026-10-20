using TermPoint.Services;

namespace TermPoint.Tests.Harness;

/// <summary>
/// What a generated run shares between its events (spec item 9.3, step 9.3c): the <see cref="World"/>, its computers,
/// the things an event leaves for a later event (the snapshot of the shared database, the colleague's edit counter),
/// and the decision source that dialogs ask. One context lives for exactly one run.
/// </summary>
internal sealed class RunContext
{
    /// <summary>The most dialogs one event may raise before every further dialog is answered with its terminal answer (a guard against dialog loops that a random "Try again" would never leave).</summary>
    public const int MaxDialogsPerEvent = 24;

    /// <summary>Serialises decisions: a dialog can be raised from a background thread.</summary>
    private readonly object _gate = new();

    /// <summary>The source that decides.</summary>
    private readonly IDecisionSource _source;

    /// <summary>How many dialogs the current event has raised so far.</summary>
    private int _dialogsThisEvent;

    /// <summary>How many edits the colleague has made.</summary>
    private int _colleagueEdits;

    /// <summary>Creates the context.</summary>
    /// <param name="world">The world of the run.</param>
    /// <param name="source">The source dialogs ask for their answers.</param>
    public RunContext(World world, IDecisionSource source)
    {
        World   = world;
        _source = source;
    }

    /// <summary>The world of the run.</summary>
    public World World { get; }

    /// <summary>The computers of the world, in order.</summary>
    public IReadOnlyList<Computer> Computers => World.Computers;

    /// <summary>Finds a computer by name.</summary>
    /// <param name="name">Lower-case first name ("bob").</param>
    /// <returns>The computer.</returns>
    public Computer Computer(string name) => World.Computers.First(c => c.Name == name);

    /// <summary>The bytes of the shared database as a snapshot took them, or null when none has been taken.</summary>
    public byte[]? Snapshot { get; set; }

    /// <summary>Hands out the next id for an edit made by the colleague ("colleague-1", ...).</summary>
    public string NextColleagueEdit() => $"colleague-{++_colleagueEdits}";

    /// <summary>Forgets the dialog count: a new event starts.</summary>
    public void BeginEvent()
    {
        lock (_gate) _dialogsThisEvent = 0;
    }

    /// <summary>
    /// Counts a dialog raised by the current event.
    /// </summary>
    /// <returns>True when the event has raised too many dialogs and the dialog must be answered with its terminal answer without asking the source.</returns>
    public bool CountDialogAndShouldForce()
    {
        lock (_gate) return ++_dialogsThisEvent > MaxDialogsPerEvent;
    }

    /// <summary>Asks the decision source how a dialog is answered.</summary>
    /// <param name="dialog">Which dialog it is.</param>
    /// <param name="options">The possible answers, simplest first.</param>
    /// <returns>One of the options.</returns>
    public string Decide(string dialog, IReadOnlyList<string> options)
    {
        lock (_gate) return _source.Answer(dialog, options);
    }
}

/// <summary>
/// One kind of thing that can happen in a generated run (spec item 9.3, step 9.3c): a person acting on a computer, or the
/// world doing something. Each event knows when it is enabled and how to carry itself out through the
/// <see cref="World"/>'s funnel, so every event is narrated in the story and checked against every invariant exactly as a
/// hand-written walk's steps are.
/// </summary>
internal abstract class HarnessEvent
{
    /// <summary>
    /// A stable text that identifies the event and its parameters ("Edit(bob)", "AdvanceTime(4m)", "Sleep(alice,10m)").
    /// Recorded runs name events by key.
    /// </summary>
    public abstract string Key { get; }

    /// <summary>The event as a sentence, for the report's list of events.</summary>
    public abstract string Story { get; }

    /// <summary>How much a random run favours this event (1 or more).</summary>
    public virtual int Weight => 2;

    /// <summary>The key of the same event with a shorter duration, or null: what the shrinker tries instead of this one.</summary>
    public virtual string? ShorterKey => null;

    /// <summary>True when the event makes sense in the state the run is in.</summary>
    /// <param name="ctx">The run.</param>
    public abstract bool IsEnabled(RunContext ctx);

    /// <summary>Carries the event out.</summary>
    /// <param name="ctx">The run.</param>
    /// <returns>A task that completes when the event, and the invariants after it, are done.</returns>
    public abstract Task ExecuteAsync(RunContext ctx);
}

/// <summary>An event that one named computer acts on.</summary>
internal abstract class ComputerEvent : HarnessEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="name">The computer's name ("bob").</param>
    /// <param name="verb">The event's name without the computer ("Edit").</param>
    /// <param name="story">The sentence template; "{0}" stands for the computer's name.</param>
    protected ComputerEvent(string name, string verb, string story)
    {
        ComputerName = name;
        Verb         = verb;
        _story       = story;
    }

    /// <summary>The story template.</summary>
    private readonly string _story;

    /// <summary>The computer's name ("bob").</summary>
    public string ComputerName { get; }

    /// <summary>The event's name without the computer.</summary>
    protected string Verb { get; }

    /// <inheritdoc/>
    public override string Key => $"{Verb}({ComputerName})";

    /// <inheritdoc/>
    public override string Story => string.Format(_story, char.ToUpperInvariant(ComputerName[0]) + ComputerName[1..]);

    /// <summary>The computer this event acts on.</summary>
    /// <param name="ctx">The run.</param>
    protected Computer On(RunContext ctx) => ctx.Computer(ComputerName);
}

/// <summary>Opens the shared database through the startup path. Enabled when the computer has no open session.</summary>
internal sealed class OpenEvent : ComputerEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="name">The computer's name.</param>
    public OpenEvent(string name) : base(name, "Open", "{0} starts TermPoint and opens the database") { }

    /// <inheritdoc/>
    public override int Weight => 6;

    /// <inheritdoc/>
    public override bool IsEnabled(RunContext ctx) => !On(ctx).HasOpenSession;

    /// <inheritdoc/>
    public override async Task ExecuteAsync(RunContext ctx)
    {
        var computer = On(ctx);
        await computer.OpenAsync();
        computer.ExitIfNothingOpen();
    }
}

/// <summary>Makes a traceable edit. Enabled when the computer's session has write access to the shared database.</summary>
internal sealed class EditEvent : ComputerEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="name">The computer's name.</param>
    public EditEvent(string name) : base(name, "Edit", "{0} makes an edit") { }

    /// <inheritdoc/>
    public override int Weight => 6;

    /// <inheritdoc/>
    public override bool IsEnabled(RunContext ctx) => On(ctx).HasLiveSession;

    /// <inheritdoc/>
    public override Task ExecuteAsync(RunContext ctx)
    {
        On(ctx).Edit();
        return Task.CompletedTask;
    }
}

/// <summary>Presses Save. Enabled when the computer has an open session.</summary>
internal sealed class SaveEvent : ComputerEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="name">The computer's name.</param>
    public SaveEvent(string name) : base(name, "Save", "{0} presses Save") { }

    /// <inheritdoc/>
    public override int Weight => 3;

    /// <inheritdoc/>
    public override bool IsEnabled(RunContext ctx) => On(ctx).HasOpenSession;

    /// <inheritdoc/>
    public override async Task ExecuteAsync(RunContext ctx) => await On(ctx).SaveAsync();
}

/// <summary>The autosave timer fires. Enabled when the computer has an open session.</summary>
internal sealed class AutosaveTickEvent : ComputerEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="name">The computer's name.</param>
    public AutosaveTickEvent(string name) : base(name, "AutosaveTick", "{0}'s autosave timer fires") { }

    /// <inheritdoc/>
    public override bool IsEnabled(RunContext ctx) => On(ctx).HasOpenSession;

    /// <inheritdoc/>
    public override async Task ExecuteAsync(RunContext ctx) => await On(ctx).AutoSaveTickAsync();
}

/// <summary>The heartbeat timer fires. Enabled when the computer is the writer.</summary>
internal sealed class HeartbeatTickEvent : ComputerEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="name">The computer's name.</param>
    public HeartbeatTickEvent(string name) : base(name, "HeartbeatTick", "{0}'s heartbeat timer fires") { }

    /// <inheritdoc/>
    public override bool IsEnabled(RunContext ctx) => On(ctx).HasOpenSession && On(ctx).Checkout.Mode == CheckoutMode.WriteAccess;

    /// <inheritdoc/>
    public override async Task ExecuteAsync(RunContext ctx) => await On(ctx).HeartbeatTickAsync();
}

/// <summary>The reader's poll timer fires. Enabled when the computer is a reader.</summary>
internal sealed class PollTickEvent : ComputerEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="name">The computer's name.</param>
    public PollTickEvent(string name) : base(name, "PollTick", "{0}'s lock poll timer fires") { }

    /// <inheritdoc/>
    public override bool IsEnabled(RunContext ctx) => On(ctx).HasOpenSession && On(ctx).Checkout.Mode == CheckoutMode.ReadOnly;

    /// <inheritdoc/>
    public override async Task ExecuteAsync(RunContext ctx) => await On(ctx).PollTickAsync();
}

/// <summary>Closes TermPoint (the window's X). Enabled when the computer has an open session.</summary>
internal sealed class CloseEvent : ComputerEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="name">The computer's name.</param>
    public CloseEvent(string name) : base(name, "Close", "{0} closes TermPoint") { }

    /// <inheritdoc/>
    public override bool IsEnabled(RunContext ctx) => On(ctx).HasOpenSession;

    /// <inheritdoc/>
    public override async Task ExecuteAsync(RunContext ctx) => await On(ctx).CloseAsync();
}

/// <summary>Switches to the second database ("other.db"). Enabled when the computer's session is on the shared database.</summary>
internal sealed class SwitchAwayEvent : ComputerEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="name">The computer's name.</param>
    public SwitchAwayEvent(string name) : base(name, "SwitchAway", "{0} switches to the other database") { }

    /// <inheritdoc/>
    public override int Weight => 1;

    /// <inheritdoc/>
    public override bool IsEnabled(RunContext ctx) => On(ctx).SessionIsOnSharedDatabase;

    /// <inheritdoc/>
    public override async Task ExecuteAsync(RunContext ctx)
    {
        var computer = On(ctx);
        await computer.SwitchToAsync(ctx.World.EnsureOtherDatabase());
        computer.ExitIfNothingOpen();
    }
}

/// <summary>Switches back to the shared database. Enabled when the computer's session is open on the second database.</summary>
internal sealed class SwitchBackEvent : ComputerEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="name">The computer's name.</param>
    public SwitchBackEvent(string name) : base(name, "SwitchBack", "{0} switches back to the shared database") { }

    /// <inheritdoc/>
    public override bool IsEnabled(RunContext ctx) => On(ctx).HasOpenSession && !On(ctx).SessionIsOnSharedDatabase;

    /// <inheritdoc/>
    public override async Task ExecuteAsync(RunContext ctx)
    {
        var computer = On(ctx);
        await computer.SwitchToAsync(ctx.World.DatabasePath);
        computer.ExitIfNothingOpen();
    }
}

/// <summary>
/// Reopens the shared database for editing from a read-only session ("Take over as writer", which goes through the same
/// switch path). Enabled when the computer is a reader on the shared database. An addition to the catalogue the brief
/// lists: it is the only way a reader becomes a writer.
/// </summary>
internal sealed class ReopenForEditingEvent : ComputerEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="name">The computer's name.</param>
    public ReopenForEditingEvent(string name) : base(name, "Reopen", "{0} asks to take over as writer") { }

    /// <inheritdoc/>
    public override bool IsEnabled(RunContext ctx)
        => On(ctx).SessionIsOnSharedDatabase && On(ctx).Checkout.Mode == CheckoutMode.ReadOnly;

    /// <inheritdoc/>
    public override async Task ExecuteAsync(RunContext ctx)
    {
        var computer = On(ctx);
        await computer.SwitchToAsync(ctx.World.DatabasePath);
        computer.ExitIfNothingOpen();
    }
}

/// <summary>Answers the save-conflict banner with "Save a Copy…" or "Discard Changes". Enabled when the banner shows.</summary>
internal sealed class ResolveConflictEvent : ComputerEvent
{
    /// <summary>True for "Save a Copy…", false for "Discard Changes".</summary>
    private readonly bool _saveCopy;

    /// <summary>Creates the event.</summary>
    /// <param name="name">The computer's name.</param>
    /// <param name="saveCopy">True for Save a Copy…, false for Discard Changes.</param>
    public ResolveConflictEvent(string name, bool saveCopy)
        : base(name, saveCopy ? "ResolveSaveCopy" : "ResolveDiscard",
               saveCopy ? "{0} chooses Save a Copy… on the conflict banner" : "{0} chooses Discard Changes on the conflict banner")
        => _saveCopy = saveCopy;

    /// <inheritdoc/>
    public override int Weight => 5;

    /// <inheritdoc/>
    public override bool IsEnabled(RunContext ctx) => On(ctx).HasOpenSession && On(ctx).ConflictBannerShown;

    /// <inheritdoc/>
    public override async Task ExecuteAsync(RunContext ctx)
    {
        var computer = On(ctx);
        await computer.ResolveConflictAsync(_saveCopy);
        computer.ExitIfNothingOpen();
    }
}

/// <summary>The computer's connection to the share drops. Enabled while it is up.</summary>
internal sealed class NetworkDownEvent : ComputerEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="name">The computer's name.</param>
    public NetworkDownEvent(string name) : base(name, "NetworkDown", "{0}'s network drops") { }

    /// <inheritdoc/>
    public override int Weight => 1;

    /// <inheritdoc/>
    public override bool IsEnabled(RunContext ctx) => !On(ctx).NetworkIsDown;

    /// <inheritdoc/>
    public override Task ExecuteAsync(RunContext ctx)
    {
        ctx.World.NetworkDown(On(ctx));
        return Task.CompletedTask;
    }
}

/// <summary>The computer's connection to the share returns. Enabled while it is down.</summary>
internal sealed class NetworkUpEvent : ComputerEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="name">The computer's name.</param>
    public NetworkUpEvent(string name) : base(name, "NetworkUp", "{0}'s network returns") { }

    /// <inheritdoc/>
    public override int Weight => 3;

    /// <inheritdoc/>
    public override bool IsEnabled(RunContext ctx) => On(ctx).NetworkIsDown;

    /// <inheritdoc/>
    public override Task ExecuteAsync(RunContext ctx)
    {
        ctx.World.NetworkUp(On(ctx));
        return Task.CompletedTask;
    }
}

/// <summary>
/// The computer sleeps for a while: its network is down and none of its timers fire while the time passes; on waking the
/// network returns, the wake check runs (which re-verifies the lock, and demotes the session if it was taken over) and,
/// if the computer is still the writer, a heartbeat is sent. Enabled when the computer has an open session and its
/// network is up.
/// </summary>
internal sealed class SleepEvent : ComputerEvent
{
    /// <summary>How long the computer sleeps.</summary>
    private readonly TimeSpan _duration;

    /// <summary>The duration as the key writes it ("4m").</summary>
    private readonly string _label;

    /// <summary>Creates the event.</summary>
    /// <param name="name">The computer's name.</param>
    /// <param name="duration">How long it sleeps.</param>
    /// <param name="label">The duration as the key writes it.</param>
    /// <param name="shorterLabel">The label of the next shorter sleep, or null.</param>
    public SleepEvent(string name, TimeSpan duration, string label, string? shorterLabel)
        : base(name, "Sleep", "{0}'s computer sleeps for " + label)
    {
        _duration  = duration;
        _label     = label;
        ShorterKey = shorterLabel is null ? null : $"Sleep({name},{shorterLabel})";
    }

    /// <inheritdoc/>
    public override string Key => $"Sleep({ComputerName},{_label})";

    /// <inheritdoc/>
    public override string? ShorterKey { get; }

    /// <inheritdoc/>
    public override int Weight => 1;

    /// <inheritdoc/>
    public override bool IsEnabled(RunContext ctx) => On(ctx).HasOpenSession && !On(ctx).NetworkIsDown;

    /// <inheritdoc/>
    public override async Task ExecuteAsync(RunContext ctx)
    {
        var computer = On(ctx);
        var world    = ctx.World;
        await world.DoAsync(null, $"{computer.DisplayName}'s computer went to sleep for {_label}", async () =>
        {
            world.NetworkDown(computer);        // nothing reaches the share while it sleeps
            world.AdvanceTime(_duration);       // and none of its timers fire
            world.NetworkUp(computer);          // it wakes
            await computer.WakeCheckAsync();
            if (computer.HasOpenSession && computer.Checkout.Mode == CheckoutMode.WriteAccess)
                await computer.HeartbeatTickAsync();
        });
    }
}

/// <summary>The computer's TermPoint is killed between actions. Enabled while its process is running.</summary>
internal sealed class CrashEvent : ComputerEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="name">The computer's name.</param>
    public CrashEvent(string name) : base(name, "Crash", "{0}'s computer crashes") { }

    /// <inheritdoc/>
    public override int Weight => 2;

    /// <inheritdoc/>
    public override bool IsEnabled(RunContext ctx) => On(ctx).IsRunning;

    /// <inheritdoc/>
    public override Task ExecuteAsync(RunContext ctx)
    {
        ctx.World.CrashComputer(On(ctx));
        return Task.CompletedTask;
    }
}

/// <summary>Starts TermPoint again after a crash, without opening anything yet. Enabled while the process is not running.</summary>
internal sealed class RelaunchEvent : ComputerEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="name">The computer's name.</param>
    public RelaunchEvent(string name) : base(name, "Relaunch", "{0} starts TermPoint again") { }

    /// <inheritdoc/>
    public override int Weight => 3;

    /// <inheritdoc/>
    public override bool IsEnabled(RunContext ctx) => !On(ctx).IsRunning;

    /// <inheritdoc/>
    public override Task ExecuteAsync(RunContext ctx)
    {
        On(ctx).Relaunch();
        return Task.CompletedTask;
    }
}

/// <summary>Time passes (the clock the lock service reads moves forward; no timer fires by itself). Always enabled.</summary>
internal sealed class AdvanceTimeEvent : HarnessEvent
{
    /// <summary>How far the clock moves.</summary>
    private readonly TimeSpan _by;

    /// <summary>The duration as the key writes it ("4m").</summary>
    private readonly string _label;

    /// <summary>Creates the event.</summary>
    /// <param name="by">How far the clock moves.</param>
    /// <param name="label">The duration as the key writes it ("30s", "4m", "10m").</param>
    /// <param name="shorterLabel">The label of the next shorter duration, or null.</param>
    public AdvanceTimeEvent(TimeSpan by, string label, string? shorterLabel)
    {
        _by        = by;
        _label     = label;
        ShorterKey = shorterLabel is null ? null : $"AdvanceTime({shorterLabel})";
    }

    /// <inheritdoc/>
    public override string Key => $"AdvanceTime({_label})";

    /// <inheritdoc/>
    public override string Story => $"Time passes ({_label})";

    /// <inheritdoc/>
    public override string? ShorterKey { get; }

    /// <inheritdoc/>
    public override int Weight => _by < TimeSpan.FromMinutes(1) ? 2 : _by < TimeSpan.FromMinutes(5) ? 3 : 1;

    /// <inheritdoc/>
    public override bool IsEnabled(RunContext ctx) => true;

    /// <inheritdoc/>
    public override Task ExecuteAsync(RunContext ctx)
    {
        ctx.World.AdvanceTime(_by);
        return Task.CompletedTask;
    }
}

/// <summary>A colleague on 1.2.2 (not one of the simulated computers) changes the shared database directly. Always enabled.</summary>
internal sealed class ColleagueWritesEvent : HarnessEvent
{
    /// <inheritdoc/>
    public override string Key => "ColleagueWrites";

    /// <inheritdoc/>
    public override string Story => "A colleague on an older version changes the shared database";

    /// <inheritdoc/>
    public override int Weight => 1;

    /// <inheritdoc/>
    public override bool IsEnabled(RunContext ctx) => true;

    /// <inheritdoc/>
    public override Task ExecuteAsync(RunContext ctx)
    {
        ctx.World.ColleagueWritesDatabase(ctx.NextColleagueEdit());
        return Task.CompletedTask;
    }
}

/// <summary>A backup tool copies the shared database aside. Enabled while no snapshot is held.</summary>
internal sealed class TakeSnapshotEvent : HarnessEvent
{
    /// <inheritdoc/>
    public override string Key => "TakeSnapshot";

    /// <inheritdoc/>
    public override string Story => "A backup tool copies the shared database aside";

    /// <inheritdoc/>
    public override int Weight => 1;

    /// <inheritdoc/>
    public override bool IsEnabled(RunContext ctx) => ctx.Snapshot is null;

    /// <inheritdoc/>
    public override Task ExecuteAsync(RunContext ctx)
    {
        ctx.World.Do(null, "A backup tool copied the shared database aside", () => { ctx.Snapshot = ctx.World.TakeSnapshot(); });
        return Task.CompletedTask;
    }
}

/// <summary>IT copies the snapshot back over the shared database. Enabled while a snapshot is held; uses it up.</summary>
internal sealed class ItRestoresOldFileEvent : HarnessEvent
{
    /// <inheritdoc/>
    public override string Key => "ItRestoresOldFile";

    /// <inheritdoc/>
    public override string Story => "IT copies the older snapshot back over the shared database";

    /// <inheritdoc/>
    public override int Weight => 1;

    /// <inheritdoc/>
    public override bool IsEnabled(RunContext ctx) => ctx.Snapshot is not null;

    /// <inheritdoc/>
    public override Task ExecuteAsync(RunContext ctx)
    {
        var snapshot = ctx.Snapshot!;
        ctx.Snapshot = null;
        ctx.World.ItRestoresOldFile(snapshot);
        return Task.CompletedTask;
    }
}

/// <summary>Somebody deletes the lock file. Enabled while it exists.</summary>
internal sealed class DeleteLockFileEvent : HarnessEvent
{
    /// <inheritdoc/>
    public override string Key => "DeleteLockFile";

    /// <inheritdoc/>
    public override string Story => "Somebody deletes the lock file from the share";

    /// <inheritdoc/>
    public override int Weight => 1;

    /// <inheritdoc/>
    public override bool IsEnabled(RunContext ctx) => ctx.World.LockFileExists;

    /// <inheritdoc/>
    public override Task ExecuteAsync(RunContext ctx)
    {
        ctx.World.DeleteLockFile();
        return Task.CompletedTask;
    }
}

/// <summary>
/// The catalogue of events (spec item 9.3, step 9.3c): for each computer, the things a person or their machine does;
/// for the world, the things that happen to the share. Built once per run in a fixed order, so the same state always
/// offers the same events in the same order (the exhaustive search depends on it).
/// </summary>
internal static class EventCatalogue
{
    /// <summary>The computers a generated run has, in order.</summary>
    public static readonly string[] ComputerNames = { "bob", "alice" };

    /// <summary>Builds every event of the catalogue.</summary>
    /// <returns>The events, in their fixed order.</returns>
    public static IReadOnlyList<HarnessEvent> Build()
    {
        var events = new List<HarnessEvent>();
        foreach (var name in ComputerNames)
        {
            events.Add(new OpenEvent(name));
            events.Add(new EditEvent(name));
            events.Add(new SaveEvent(name));
            events.Add(new AutosaveTickEvent(name));
            events.Add(new HeartbeatTickEvent(name));
            events.Add(new PollTickEvent(name));
            events.Add(new CloseEvent(name));
            events.Add(new SwitchAwayEvent(name));
            events.Add(new SwitchBackEvent(name));
            events.Add(new ReopenForEditingEvent(name));
            events.Add(new ResolveConflictEvent(name, saveCopy: true));
            events.Add(new ResolveConflictEvent(name, saveCopy: false));
            events.Add(new NetworkDownEvent(name));
            events.Add(new NetworkUpEvent(name));
            events.Add(new SleepEvent(name, TimeSpan.FromMinutes(4), "4m", null));
            events.Add(new SleepEvent(name, TimeSpan.FromMinutes(10), "10m", "4m"));
            events.Add(new CrashEvent(name));
            events.Add(new RelaunchEvent(name));
        }

        events.Add(new AdvanceTimeEvent(TimeSpan.FromSeconds(30), "30s", null));
        events.Add(new AdvanceTimeEvent(TimeSpan.FromMinutes(4), "4m", "30s"));
        events.Add(new AdvanceTimeEvent(TimeSpan.FromMinutes(10), "10m", "4m"));
        events.Add(new ColleagueWritesEvent());
        events.Add(new TakeSnapshotEvent());
        events.Add(new ItRestoresOldFileEvent());
        events.Add(new DeleteLockFileEvent());
        return events;
    }

    /// <summary>Every event key of the catalogue, in order (for the coverage hint "events never enabled in any run").</summary>
    public static IReadOnlyList<string> AllKeys() => Build().Select(e => e.Key).ToList();
}
