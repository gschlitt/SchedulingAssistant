using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TermPoint.Models;
using TermPoint.Services;

namespace TermPoint.Tests.Harness;

/// <summary>
/// One simulated computer of the multi-computer harness (spec item 9.3, steps 9.3a and 9.3b): Bob's laptop, Alice's PC.
/// It owns a private local folder (working copies and the single-instance guard) and a full TermPoint stack wired like
/// <c>MainWindow</c> wires it: a <see cref="WriteLockService"/> with this computer's <see cref="LockIdentity"/> and no
/// timers, a <see cref="CheckoutService"/>, a <see cref="DatabaseInstanceGuard"/>, a <see cref="ScriptedPrompts"/>
/// standing in for every dialog, a <see cref="FakeSessionHost"/> standing in for the window and the container, and the
/// real <see cref="SessionLifecycle"/> on top.
///
/// <para><b>Actions.</b> Each action (<see cref="OpenAsync"/>, <see cref="Edit"/>, <see cref="SaveAsync"/>, ...) goes
/// through the World's funnel (<see cref="World.DoAsync{T}"/>), which writes it into the story and runs every invariant
/// when it ends. Inside, it runs with this computer set as the World's current actor, with the process-wide settings in
/// their standard state, and — when it ends — waits for the background work it started (a lost-lock demotion, the lock
/// service's deletion of its lock file). Timers never run by themselves: <see cref="AutoSaveTickAsync"/> and
/// <see cref="HeartbeatTickAsync"/> run one tick when the walk says so.</para>
///
/// <para><b>Launches.</b> The stack belongs to one process (one process id). <see cref="CloseAsync"/> ends it
/// cleanly; <see cref="World.CrashComputer"/> kills it. The next <see cref="OpenAsync"/> (or an explicit
/// <see cref="Relaunch"/>, when the walk first needs the new prompts to script) starts a fresh stack on the same
/// folders with a new process id, as a real relaunch would.</para>
/// </summary>
internal sealed class Computer : IDisposable
{
    /// <summary>The world this computer lives in.</summary>
    private readonly World _world;

    /// <summary>Lost-lock demotions (and any other background work) started by events during an action.</summary>
    private readonly List<Task> _pending = new();

    /// <summary>The identity of the current process (machine, user, pid, start time).</summary>
    private LockIdentity _identity = null!;

    /// <summary>How many traceable edits this computer has made so far (numbers "bob-1", "bob-2", ...).</summary>
    private int _editCount;

    /// <summary>True between a launch and the process ending (closed or killed).</summary>
    private bool _running;

    /// <summary>Creates the computer, its folders and its first running process.</summary>
    /// <param name="world">The world it lives in.</param>
    /// <param name="name">Lower-case first name ("bob").</param>
    public Computer(World world, string name)
    {
        _world    = world;
        Name      = name;
        LocalDir  = Path.Combine(world.RootDir, "computers", name);
        WorkDir   = Path.Combine(LocalDir, "work");
        GuardDir  = Path.Combine(LocalDir, "guard");
        Directory.CreateDirectory(WorkDir);
        Directory.CreateDirectory(GuardDir);
        Launch();
    }

    // ── Identity and state ───────────────────────────────────────────────────

    /// <summary>Lower-case first name; also the user name in the lock file and the prefix of the edit ids.</summary>
    public string Name { get; }

    /// <summary>The first name with a capital letter, as the story writes it ("Bob").</summary>
    public string DisplayName => char.ToUpperInvariant(Name[0]) + Name[1..];

    /// <summary>The machine name written into the lock file ("BOB-PC").</summary>
    public string MachineName => $"{Name.ToUpperInvariant()}-PC";

    /// <summary>The computer's private folder: working copies, dirty markers and the instance guard.</summary>
    public string LocalDir { get; }

    /// <summary>The folder for D', D'' and the dirty marker (the checkout service's working directory).</summary>
    public string WorkDir { get; }

    /// <summary>The folder of the single-instance guard files.</summary>
    public string GuardDir { get; }

    /// <summary>The id of the current (or last) process.</summary>
    public int ProcessId => _identity.ProcessId;

    /// <summary>True between a launch and the process ending (closed or killed).</summary>
    public bool IsRunning => _running;

    /// <summary>True while this computer's connection to the share is cut (see <see cref="World.NetworkDown"/>).</summary>
    public bool NetworkIsDown { get; internal set; }

    /// <summary>
    /// True once a save was refused because the shared database changed elsewhere (the conflict banner of
    /// <c>MainWindow</c>); cleared when a launch starts and when the conflict is resolved.
    /// </summary>
    public bool ConflictBannerShown { get; private set; }

    /// <summary>The ids of the edits this computer has applied, in order ("bob-1", "bob-2", ...), across all its launches.</summary>
    public List<string> AppliedEdits { get; } = new();

    /// <summary>The ordered trace of host calls, prompts and saves of the current process (for debugging a failing walk).</summary>
    public List<string> Trace { get; private set; } = new();

    /// <summary>This process's logger (a recording one: nothing reaches a file).</summary>
    public RecordingAppLogger Logger { get; private set; } = null!;

    /// <summary>This process's write-lock service.</summary>
    public WriteLockService Locks { get; private set; } = null!;

    /// <summary>This process's checkout service.</summary>
    public CheckoutService Checkout { get; private set; } = null!;

    /// <summary>This process's single-instance guard.</summary>
    public DatabaseInstanceGuard Guard { get; private set; } = null!;

    /// <summary>
    /// This process's scripted dialogs. A walk scripts the answers it expects BEFORE the action that will ask them;
    /// <see cref="ScriptedPrompts.Calls"/> records everything that was asked, with exact titles, texts and answers. A
    /// relaunch creates a new one.
    /// </summary>
    public ScriptedPrompts Prompts { get; private set; } = null!;

    /// <summary>This process's fake window and container (its <see cref="FakeSessionHost.LockLossSurface"/> keeps the banners).</summary>
    public FakeSessionHost Host { get; private set; } = null!;

    /// <summary>This process's session lifecycle (the production class, unchanged).</summary>
    public SessionLifecycle Lifecycle { get; private set; } = null!;

    // ── Launching ────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts a fresh process on this computer's folders: a new process id and a new full stack, wired like the main
    /// window wires it (<see cref="CheckoutService.WriteLockLost"/> to the lifecycle's lost-lock handler,
    /// <see cref="CheckoutService.SaveConflictDetected"/> to <see cref="ConflictBannerShown"/>). Does nothing when a
    /// process is already running. Use it directly when a walk must script the new process's prompts before opening.
    /// </summary>
    public void Relaunch()
    {
        if (!_running) _world.Do(this, $"{DisplayName} restarted TermPoint", Launch);
    }

    /// <summary>Builds the stack for a new process and marks it running.</summary>
    private void Launch()
    {
        var pid = _world.StartProcess(out var started);
        _identity = new LockIdentity(MachineName, Name, pid, started, _world.LookupProcess);

        Trace         = new List<string>();
        Logger        = new RecordingAppLogger();
        Locks         = new WriteLockService(_world.Clock, _identity, startTimers: false);
        Checkout      = new CheckoutService(Locks, Logger, action => action(), WorkDir, startTimers: false);
        Guard         = new DatabaseInstanceGuard(GuardDir, Logger);
        Prompts       = new ScriptedPrompts(Trace.Add);
        Host          = new FakeSessionHost(Trace.Add, Logger, Checkout, () => null);
        Lifecycle     = new SessionLifecycle(
            Checkout, Guard, () => Logger, () => AppSettings.Current, _ => { }, () => null, Prompts, Host);

        Checkout.WriteLockLost        += reason => _pending.Add(Lifecycle.HandleWriteLockLostAsync(reason));
        Checkout.SaveConflictDetected += () => ConflictBannerShown = true;

        // Everything a person would read reaches the world: dialogs (with their answers) and banners.
        Prompts.OnCall                   = call => _world.ObservePrompt(this, call);
        Host.LockLossSurface.OnSaveError = text => _world.ObserveBanner(this, text);
        Checkout.SaveFailed             += (text, _) => _world.ObserveBanner(this, text);

        ConflictBannerShown = false;
        _running = true;
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the shared database through the startup path (<see cref="SessionLifecycle.OpenStartupDatabaseAsync"/>),
    /// then starts autosave if the settings want it, as <c>MainWindow</c> does after wiring the window. Starts a new
    /// process first if the last one has ended.
    /// </summary>
    /// <returns>What the lifecycle returned: null when the open finished (or the app is closing).</returns>
    public Task<string?> OpenAsync()
        => _world.DoAsync(this, $"{DisplayName} opened {Path.GetFileName(_world.DatabasePath)} for editing",
            () => ActAsync(async () =>
            {
                Relaunch();
                var next = await Lifecycle.OpenStartupDatabaseAsync(_world.DatabasePath, AppSettings.Current);
                StartAutoSaveLikeTheWindow();
                _world.NoteOpened(this);
                return next;
            }),
            _ => DescribeMode());

    /// <summary>Switches to another database (<see cref="SessionLifecycle.SwitchDatabaseAsync"/>).</summary>
    /// <param name="path">Full path of the database to switch to.</param>
    /// <returns>True only when the requested database ended up open.</returns>
    public Task<bool> SwitchToAsync(string path)
        => _world.DoAsync(this, $"{DisplayName} switched to {Path.GetFileName(path)}",
            () => ActAsync(async () =>
            {
                var opened = await Lifecycle.SwitchDatabaseAsync(path);
                StartAutoSaveLikeTheWindow();
                _world.NoteOpened(this);
                return opened;
            }),
            opened => opened ? DescribeMode() : "it did not open");

    /// <summary>
    /// Makes the next traceable edit: inserts a row ("bob-1", "bob-2", ...) into the harness table through the
    /// session's live connection on its working file, and marks the session dirty — exactly the way a first write
    /// through the real data layer reaches the checkout service, so the unsaved-changes marker appears.
    /// </summary>
    /// <returns>The id of the edit that was made.</returns>
    /// <exception cref="InvalidOperationException">No connection is open (nothing is open, or the session was closed).</exception>
    public string Edit()
    {
        var id = $"{Name}-{_editCount + 1}";
        _world.Do(this, $"{DisplayName} made edit {id}", () =>
        {
            ActAsync(() =>
            {
                Host.EditWith($"INSERT INTO {World.EditsTable}(id, actor) VALUES ('{id}', '{Name}')");
                return Task.FromResult(0);
            }).GetAwaiter().GetResult();

            _editCount++;
            AppliedEdits.Add(id);
        });
        return id;
    }

    /// <summary>Saves the working copy to the shared database (the Save button: <see cref="CheckoutService.SaveAsync"/>).</summary>
    /// <returns>How the save ended.</returns>
    public Task<SaveOutcome> SaveAsync()
        => _world.DoAsync(this, $"{DisplayName} saved the database",
            () => ActAsync(() => Checkout.SaveAsync()), outcome => outcome.ToString());

    /// <summary>Runs one autosave tick now (what the autosave timer does every few minutes).</summary>
    /// <returns>A task that completes when the tick's save, or its skip, is done.</returns>
    public Task AutoSaveTickAsync()
        => _world.DoAsync(this, $"{DisplayName}'s autosave timer fired",
            () => ActAsync(async () => { await Checkout.TickAutoSaveAsync(); return 0; }));

    /// <summary>Runs one lock-heartbeat tick now (what the 60-second heartbeat timer does).</summary>
    /// <returns>A task that completes when the heartbeat write, or its failure, is done.</returns>
    public Task HeartbeatTickAsync()
        => _world.DoAsync(this, $"{DisplayName}'s heartbeat ticked",
            () => ActAsync(async () => { await Locks.TickHeartbeatAsync(); return 0; }));

    /// <summary>
    /// Closes TermPoint: the window-close teardown (<see cref="SessionLifecycle.TeardownForCloseAsync"/> — final save,
    /// release of the lock, cleanup of the working copy) and then the process ends cleanly. The next
    /// <see cref="OpenAsync"/> is a new process.
    /// </summary>
    /// <returns>A task that completes when the process has ended.</returns>
    public Task CloseAsync()
        => _world.DoAsync(this, $"{DisplayName} closed TermPoint", async () =>
        {
            await ActAsync(async () =>
            {
                await Lifecycle.TeardownForCloseAsync();
                return 0;
            });
            EndProcessCleanly();
        });

    /// <summary>
    /// Carries out a choice from the save-conflict banner (<see cref="SessionLifecycle.ResolveSaveConflictAsync"/>).
    /// </summary>
    /// <param name="saveCopy">True for Save a Copy…, false for Discard Changes.</param>
    /// <returns>A task that completes when the session has been ended and the database reopened.</returns>
    public Task ResolveConflictAsync(bool saveCopy)
        => _world.DoAsync(this,
            $"{DisplayName} chose \"{(saveCopy ? "Save a Copy…" : "Discard Changes")}\" on the conflict banner",
            () => ActAsync(async () =>
            {
                await Lifecycle.ResolveSaveConflictAsync(saveCopy);
                StartAutoSaveLikeTheWindow();
                if (!Checkout.HasSaveConflict) ConflictBannerShown = false;
                _world.NoteOpened(this);
                return 0;
            }));

    /// <summary>
    /// Restores the shared database from a backup file through the checkout service (the shared safe restore, without
    /// the window around it). A restore that lands (Restored, or RenameUnconfirmed) resets invariant I1's baseline and
    /// makes the session stale: the walk should end it (crash or close) afterwards.
    /// </summary>
    /// <param name="backupPath">Full path of the backup file.</param>
    /// <returns>The restore's result.</returns>
    public Task<RestoreResult> RestoreFromBackupAsync(string backupPath)
        => _world.DoAsync(this, $"{DisplayName} restored the database from a backup",
            () => ActAsync(async () =>
            {
                var result = await Checkout.RestoreFromBackupAsync(backupPath);
                if (result.Outcome is RestoreOutcome.Restored or RestoreOutcome.RenameUnconfirmed)
                    _world.NoteRestoreLanded();
                return result;
            }),
            result => result.Outcome.ToString());

    /// <summary>Says what kind of access the last open ended with, for the story.</summary>
    private string DescribeMode()
        => Checkout.Mode == CheckoutMode.WriteAccess ? "it has write access" : "it is read-only";

    // ── What is on this computer's disk ──────────────────────────────────────

    /// <summary>The path D' would have for the shared database: the name the checkout service gives the working copy.</summary>
    public string WorkingFile
    {
        get
        {
            var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(CheckoutService.NormalizeSourcePath(_world.DatabasePath))))[..8];
            return Path.Combine(WorkDir, $"{hash}_{Path.GetFileName(_world.DatabasePath)}");
        }
    }

    /// <summary>The path of the dirty marker (the "unsaved changes" flag) beside D'.</summary>
    public string MarkerFile => WorkingFile + ".dirty";

    /// <summary>True when D' exists on this computer's disk.</summary>
    public bool HasWorkingFile => File.Exists(WorkingFile);

    /// <summary>True when the unsaved-changes marker exists on this computer's disk.</summary>
    public bool HasDirtyMarker => File.Exists(MarkerFile);

    /// <summary>
    /// True when D' is the file of a live write session: the process is running, the session holds write access, its
    /// connection is open, and the session's working file is D' (not a read-only snapshot D'').
    /// </summary>
    public bool HasLiveSession
        => _running
           && Host.ConnectionOpen
           && Checkout.Mode == CheckoutMode.WriteAccess
           && !string.IsNullOrEmpty(Checkout.WorkingPath)
           && string.Equals(Path.GetFullPath(Checkout.WorkingPath), Path.GetFullPath(WorkingFile), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The fingerprint recorded in the marker as "pending save" (a rename whose outcome was not confirmed), or null when
    /// there is no marker, none is recorded, or the marker cannot be read. Part of what invariant I4 accepts as "the
    /// version this computer last saw".
    /// </summary>
    /// <returns>The pending-save hash, or null.</returns>
    public string? PendingSaveHashInMarker()
    {
        try
        {
            if (!File.Exists(MarkerFile)) return null;
            using var stream = new FileStream(MarkerFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<DirtyMarkerData>(reader.ReadToEnd())?.PendingSaveHash;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The names of the files in this computer's working folder, sorted (D', D'', markers, snapshots).</summary>
    /// <returns>File names without folders.</returns>
    public List<string> WorkFiles()
        => Directory.GetFiles(WorkDir).Select(f => Path.GetFileName(f)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// The ids of the traceable edits in this computer's working file D' (kept or live), in insertion order; empty when
    /// there is no D'. Read from a copy.
    /// </summary>
    /// <returns>The edit ids in D'.</returns>
    public List<string> WorkingFileEdits() => _world.EditsInFile(WorkingFile);

    // ── Process ending ───────────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="World.CrashComputer"/>: the process was killed. Releases only the operating-system handles
    /// (the SQLite connection and the single-instance guard) and marks the process dead. Nothing is saved, released or
    /// deleted: the lock service and the checkout service are simply abandoned.
    /// </summary>
    internal void AbandonProcess()
    {
        Host.Dispose();
        Guard.Dispose();
        _pending.Clear();
        _world.EndProcess(_identity.ProcessId);
        _running = false;
    }

    /// <summary>The process ended by itself after the window closed: free its handles and mark it gone.</summary>
    private void EndProcessCleanly()
    {
        Host.Dispose();
        Guard.Dispose();
        _world.EndProcess(_identity.ProcessId);
        _running = false;
    }

    /// <summary>Frees the current process's handles (end of the test); safe to call after the process has ended.</summary>
    public void Dispose()
    {
        if (!_running) return;
        Host.Dispose();
        Guard.Dispose();
        Checkout.Dispose();
        Locks.Dispose();
        _running = false;
    }

    // ── Internals ────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts autosave after a database has been opened for writing, as the main window does when it is set up
    /// (<c>AppSettings.AutoSaveEnabled</c>). With no timers, this only turns the "autosave is on" flag on.
    /// </summary>
    private void StartAutoSaveLikeTheWindow()
    {
        if (AppSettings.Current.AutoSaveEnabled && Checkout.Mode == CheckoutMode.WriteAccess)
            Checkout.StartAutoSave();
    }

    /// <summary>
    /// Runs one action as this computer: sets the World's current actor and the standard settings, runs the body, waits
    /// for the background work the action started (lost-lock demotions), lets the lock service finish deleting its lock
    /// file, and puts the previous actor back.
    /// </summary>
    /// <typeparam name="T">The action's result type.</typeparam>
    /// <param name="body">The action.</param>
    /// <returns>The action's result.</returns>
    private async Task<T> ActAsync<T>(Func<Task<T>> body)
    {
        var previous = _world.CurrentActor;
        _world.CurrentActor = this;
        _world.ApplySettings();
        try
        {
            var result = await body();
            while (_pending.Count > 0)
            {
                var next = _pending[0];
                _pending.RemoveAt(0);
                await next;
            }
            return result;
        }
        finally
        {
            Locks.PendingRelease?.Wait(TimeSpan.FromSeconds(10));
            _world.CurrentActor = previous;
        }
    }
}
