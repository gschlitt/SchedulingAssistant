using System.Text.Json.Nodes;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Models;

namespace TermPoint.Services;

/// <summary>One line of the history list: a step that can be undone.</summary>
/// <param name="Id">The step's id (pass it to <see cref="SectionHistory.UndoTo"/>).</param>
/// <param name="Label">The step's approved label ("Move CHEM 101 B Thu 10:00 → Fri 13:00").</param>
/// <param name="At">When the step was made.</param>
public sealed record HistoryStep(long Id, string Label, DateTime At);

/// <summary>The outcome of an undo or redo.</summary>
/// <param name="Done">True when at least one step was undone or redone; false when nothing changed.</param>
/// <param name="Message">What to tell the user ("Undone: ...", or why nothing happened).</param>
/// <param name="SectionIdToSelect">The first section of the last step that still exists, to select in the list; or null.</param>
public sealed record HistoryResult(bool Done, string Message, string? SectionIdToSelect);

/// <summary>
/// The undo history for section changes of one database (design: <c>designs/1.2.5/undo.md</c>). Wraps the data layer's
/// <see cref="ISectionChangeLog"/> with what the UI needs: recording a step around each user action, undo, redo, undo
/// to a point, the "unsaved" state, trimming after a save, and the approved wording (<see cref="HistoryText"/>).
///
/// <para><b>UI thread only.</b> The log lives on the context's one connection, which is not used from other threads.</para>
///
/// <para><b>Unsaved state.</b> Mirrors today's indicator (<c>BecameDirty</c> on, <c>SaveCompleted</c> off) and adds what
/// undo knows: <see cref="HasUnsavedChanges"/> is false after every section change was undone and nothing else changed,
/// even though the working copy's file still differs from the saved one (the dirty marker is never removed by undo, so
/// no new deletion path exists). <see cref="IDatabaseContext.ResetDirty"/> is called then so the next write raises
/// <c>BecameDirty</c> again.</para>
///
/// <para><b>Fail safe.</b> With no log (a read-only session, the demo, a failed install) the service reports
/// <see cref="IsAvailable"/> false, <see cref="Record"/> just runs the write, and nothing can be undone. An undo checks
/// each section against what the step left (apart from references to things deleted since); a section that was changed
/// another way cuts the history instead of undoing the wrong thing.</para>
/// </summary>
public sealed class SectionHistory : IDisposable
{
    /// <summary>Shown when an undo's write fails for a reason other than lost write access. approved wording (Greg, 2026-11-26).</summary>
    private const string WriteFailedMessage = "The undo could not be completed. No changes were made. Please try again.";

    private readonly IDatabaseContext _db;
    private readonly ISectionChangeLog? _log;
    private readonly SectionStore _store;
    private readonly ISectionRepository _sectionRepo;
    private readonly SemesterContext _semesters;
    private readonly Func<ISectionCardState> _cardState;
    private readonly ISaveSignals? _signals;
    private readonly IAppLogger _logger;

    /// <summary>Mirror of today's unsaved indicator: true after <c>BecameDirty</c>, false after <c>SaveCompleted</c>.</summary>
    private bool _dirty;

    /// <summary>The steps as of the last <see cref="Refresh"/>, oldest first.</summary>
    private IReadOnlyList<UndoStepInfo> _steps = [];

    private bool _historyIncomplete;
    private bool _hasUnsaved;
    private DateTime? _unsavedSince;
    private Exception? _loggedInstallError;

    /// <summary>
    /// Creates the history over a database context and subscribes to the save signals and the log's reset. Marks the
    /// history incomplete when the session starts with a dirty marker (restored changes are unsaved but not undoable).
    /// </summary>
    /// <param name="db">The database context; its <see cref="IDatabaseContext.ChangeLog"/> is the log (null in the demo).</param>
    /// <param name="store">The section store reloaded after an undo.</param>
    /// <param name="sectionRepo">Reads the selected semesters' sections for that reload.</param>
    /// <param name="semesters">Says which semesters are shown (the reload, and the "not shown" note).</param>
    /// <param name="cardState">
    /// Returns the Section List's card state: an open card refuses an undo. A delegate, read only when an undo runs, so
    /// the Section List (which itself records through this history) can be created after it without a dependency cycle.
    /// </param>
    /// <param name="signals">The checkout service's save signals; null for the demo.</param>
    /// <param name="logger">Where warnings and errors go.</param>
    internal SectionHistory(
        IDatabaseContext db, SectionStore store, ISectionRepository sectionRepo, SemesterContext semesters,
        Func<ISectionCardState> cardState, ISaveSignals? signals, IAppLogger logger)
    {
        _db = db;
        _log = db.ChangeLog;
        _store = store;
        _sectionRepo = sectionRepo;
        _semesters = semesters;
        _cardState = cardState;
        _signals = signals;
        _logger = logger;
        LastSaveTime = DateTime.Now;

        if (_log is not null) _log.Reset += OnLogReset;
        if (_signals is not null)
        {
            _signals.BecameDirty += OnBecameDirty;
            _signals.SaveCompleted += OnSaveCompleted;
            _signals.SaveSucceeded += OnSaveSucceeded;
            _dirty = _signals.HasDirtyMarker;
        }

        ReportInstallError();
        if (_dirty && IsAvailable) _log!.MarkHistoryIncomplete();
        Reevaluate();
    }

    /// <summary>
    /// Convenience overload for a card state that already exists (tests); forwards to the delegate constructor.
    /// </summary>
    /// <param name="db">The database context.</param>
    /// <param name="store">The section store reloaded after an undo.</param>
    /// <param name="sectionRepo">Reads the selected semesters' sections for that reload.</param>
    /// <param name="semesters">Says which semesters are shown.</param>
    /// <param name="cardState">The Section List's card state.</param>
    /// <param name="signals">The checkout service's save signals; null for the demo.</param>
    /// <param name="logger">Where warnings and errors go.</param>
    internal SectionHistory(
        IDatabaseContext db, SectionStore store, ISectionRepository sectionRepo, SemesterContext semesters,
        ISectionCardState cardState, ISaveSignals? signals, IAppLogger logger)
        : this(db, store, sectionRepo, semesters, () => cardState, signals, logger)
    {
    }

    /// <summary>True once the history has been disposed (its database is closing).</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Unsubscribes from the save signals and the log, then raises <see cref="Changed"/> once more so listeners (the
    /// main view model's two-hour notice timer) see <see cref="IsDisposed"/> and let go.
    /// </summary>
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        if (_log is not null) _log.Reset -= OnLogReset;
        if (_signals is not null)
        {
            _signals.BecameDirty -= OnBecameDirty;
            _signals.SaveCompleted -= OnSaveCompleted;
            _signals.SaveSucceeded -= OnSaveSucceeded;
        }
        Changed?.Invoke();
    }

    // ── State ────────────────────────────────────────────────────────────────

    /// <summary>Raised (UI thread) whenever anything below may have changed.</summary>
    public event Action? Changed;

    /// <summary>True when there is a log on the current connection: a writer session of the desktop build.</summary>
    public bool IsAvailable => _log?.IsAvailable == true;

    /// <summary>True when there is a step to undo.</summary>
    public bool CanUndo => _steps.Any(s => !s.IsUndone);

    /// <summary>True when there is a step to redo.</summary>
    public bool CanRedo => _steps.Any(s => s.IsUndone);

    /// <summary>The label of the step Undo would undo, or null.</summary>
    public string? NextUndoLabel => _steps.LastOrDefault(s => !s.IsUndone)?.Label;

    /// <summary>The label of the step Redo would redo (the oldest undone one), or null.</summary>
    public string? NextRedoLabel => _steps.FirstOrDefault(s => s.IsUndone)?.Label;

    /// <summary>The steps that can be undone (not undone yet), newest first: the history list.</summary>
    public IReadOnlyList<HistoryStep> UndoableSteps =>
        _steps.Where(s => !s.IsUndone).Reverse().Select(s => new HistoryStep(s.Id, s.Label, s.At)).ToList();

    /// <summary>
    /// True when the history reaches back to the last save; false after a cut or after a recovered open (then the list's
    /// last line says earlier changes can't be undone one by one).
    /// </summary>
    public bool HistoryReachesLastSave => IsAvailable && !_historyIncomplete;

    /// <summary>The session's start, then the moment of each save that kept no marker: the list's "Your last save" line.</summary>
    public DateTime LastSaveTime { get; private set; }

    /// <summary>
    /// True once a save that kept no marker has succeeded in this session: until then <see cref="LastSaveTime"/> is only
    /// the session's start, and the close question names no save time.
    /// </summary>
    public bool SavedThisSession { get; private set; }

    /// <summary>
    /// True when something is unsaved. Without a log it is today's indicator; with one, the indicator AND (a step not
    /// undone, a change undo does not cover, or an incomplete history), so undoing everything clears it.
    /// </summary>
    public bool HasUnsavedChanges => _hasUnsaved;

    /// <summary>When <see cref="HasUnsavedChanges"/> last became true; null when it is false.</summary>
    public DateTime? UnsavedSince => _unsavedSince;

    // ── Recording ────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs <paramref name="write"/> as one undo step labelled <paramref name="label"/>. Callers wrap the user action's
    /// existing repository writes unchanged; the log's triggers record them. Never call this inside an open database
    /// transaction (a rollback would roll the step back too, and the log may not nest). With no log, or when the log is
    /// busy (a step or pause is already active: a warning is logged), the write simply runs unrecorded.
    /// </summary>
    /// <param name="label">The approved label (see <see cref="HistoryText"/>).</param>
    /// <param name="write">The user action's writes.</param>
    /// <exception cref="Exception">Whatever <paramref name="write"/> throws; the step is still ended.</exception>
    public void Record(string label, Action write) => Record<object?>(label, () => { write(); return null; });

    /// <summary>
    /// <see cref="Record(string, Action)"/> for a write that returns a value.
    /// </summary>
    /// <typeparam name="T">The write's result type.</typeparam>
    /// <param name="label">The approved label.</param>
    /// <param name="write">The user action's writes.</param>
    /// <returns>What <paramref name="write"/> returned.</returns>
    /// <exception cref="Exception">Whatever <paramref name="write"/> throws; the step is still ended.</exception>
    public T Record<T>(string label, Func<T> write)
    {
        if (!IsAvailable) return write();

        var log = _log!;
        long stepId;
        try
        {
            stepId = log.BeginStep(label, DateTime.Now);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"SectionHistory: could not start a step ({ex.Message}); the change is not undoable.", "SectionHistory.Record");
            return write();
        }

        try
        {
            return write();
        }
        finally
        {
            log.EndStep(stepId, _signals?.EditGeneration ?? 0);
            Reevaluate();
        }
    }

    /// <summary>
    /// Pauses recording for section writes that are not section edits (a room delete's cleanup, Copy Semester, imports,
    /// semester deletes): nothing is recorded or undone, but the change counts as unsaved. Dispose to end the pause. When
    /// there is no log, or a step or pause is already active (a warning is logged), the returned object does nothing.
    /// </summary>
    /// <returns>An object whose disposal ends the pause.</returns>
    public IDisposable PauseRecording()
    {
        if (!IsAvailable) return NoPause.Instance;
        try
        {
            _log!.BeginPause(ChangeLogPause.NotAStep);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"SectionHistory: could not pause recording ({ex.Message}).", "SectionHistory.PauseRecording");
            return NoPause.Instance;
        }
        return new ActivePause(this);
    }

    // ── Undo and redo ────────────────────────────────────────────────────────

    /// <summary>
    /// Undoes the newest step that is not undone. Refused (Done false) when nothing can be undone, or while a section
    /// card is open (the reload would close it and lose what was typed; the message is
    /// <see cref="OpenCardMessages.WhyRefused"/>'s).
    /// </summary>
    /// <returns>The outcome, with the message to show.</returns>
    public HistoryResult Undo()
    {
        var step = _steps.LastOrDefault(s => !s.IsUndone);
        return step is null ? Nothing(HistoryText.NothingToUndo) : Run(new[] { step }, undo: true);
    }

    /// <summary>
    /// Redoes the oldest undone step (the one undone last). Refused like <see cref="Undo"/>.
    /// </summary>
    /// <returns>The outcome, with the message to show.</returns>
    public HistoryResult Redo()
    {
        var step = _steps.FirstOrDefault(s => s.IsUndone);
        return step is null ? Nothing(HistoryText.NothingToRedo) : Run(new[] { step }, undo: false);
    }

    /// <summary>
    /// Undoes every step from the newest back to <paramref name="stepId"/> inclusive, newest first, stopping at the first
    /// refusal (then <see cref="HistoryResult.Message"/> is the refusal, and <see cref="HistoryResult.Done"/> is true if
    /// earlier steps were undone).
    /// </summary>
    /// <param name="stepId">The step to go back to: it and every newer step are undone.</param>
    /// <returns>The outcome.</returns>
    public HistoryResult UndoTo(long stepId)
    {
        var steps = _steps.Where(s => !s.IsUndone && s.Id >= stepId).Reverse().ToList();
        if (steps.Count == 0 || steps[^1].Id != stepId) return Nothing(HistoryText.NothingToUndo);
        return Run(steps, undo: true);
    }

    /// <summary>Undoes every step back to the last save (the list's last line).</summary>
    /// <returns>The outcome.</returns>
    public HistoryResult UndoAll()
    {
        var steps = _steps.Where(s => !s.IsUndone).Reverse().ToList();
        return steps.Count == 0 ? Nothing(HistoryText.NothingToUndo) : Run(steps, undo: true);
    }

    /// <summary>A not-done outcome that changed nothing.</summary>
    private static HistoryResult Nothing(string message) => new(false, message, null);

    /// <summary>
    /// Undoes or redoes the given steps in order, one at a time, after the guards. Stops at the first refusal.
    /// </summary>
    /// <param name="steps">The steps, in the order to apply them.</param>
    /// <param name="undo">True for undo; false for redo.</param>
    /// <returns>The combined outcome: the last step's label, every sentence about what was not put back.</returns>
    private HistoryResult Run(IReadOnlyList<UndoStepInfo> steps, bool undo)
    {
        if (!IsAvailable) return Nothing(undo ? HistoryText.NothingToUndo : HistoryText.NothingToRedo);
        if (OpenCardMessages.WhyRefused(_cardState()) is { } refusal) return Nothing(refusal);

        var sentences = new List<string>();
        string? last = null, select = null, notShown = null;
        foreach (var step in steps)
        {
            var result = Apply(step, undo);
            if (result.Refusal is not null)
                return last is null ? Nothing(result.Refusal) : new HistoryResult(true, result.Refusal, select);
            last = step.Label;
            select = result.SectionIdToSelect ?? select;
            notShown = result.NotShownSemester;
            sentences.AddRange(result.Sentences);
        }

        var message = undo
            ? HistoryText.Undone(last!, notShown, sentences)
            : HistoryText.Redone(last!, notShown, sentences);
        return new HistoryResult(true, message, select);
    }

    /// <summary>What <see cref="Apply"/> found and did for one step.</summary>
    /// <param name="Refusal">Why the step was not applied; null when it was.</param>
    /// <param name="SectionIdToSelect">The step's first section that still exists.</param>
    /// <param name="NotShownSemester">The step's semester when it is not shown; else null.</param>
    /// <param name="Sentences">Sentences about what was not put back.</param>
    private sealed record StepOutcome(
        string? Refusal, string? SectionIdToSelect, string? NotShownSemester, IReadOnlyList<string> Sentences);

    /// <summary>
    /// Undoes or redoes one step. Checks every section it touched against the step's result first (so nothing is
    /// written unless all match); leaves out references to things deleted since; keeps a section deleted when its course
    /// or semester is gone; writes the rows in one transaction after <see cref="IDatabaseContext.MarkDirty"/>; reloads the
    /// section store.
    /// </summary>
    /// <param name="step">The step.</param>
    /// <param name="undo">True to undo (write the old rows back); false to redo (write the new rows).</param>
    /// <returns>What happened; a refusal changes nothing (a history cut empties the history).</returns>
    private StepOutcome Apply(UndoStepInfo step, bool undo)
    {
        var log = _log!;
        var groups = log.GetEntries(step.Id).GroupBy(e => e.SectionId).Select(g => g.ToList()).ToList();

        // Pass 1: every section must still be exactly what the step left (undo) or what it started from (redo).
        var items = new List<(string Id, SectionRow? Target, SectionRow? Current, SectionRow? Expected)>();
        foreach (var g in groups)
        {
            var id = g[0].SectionId;
            var target = undo ? g[0].Old : g[^1].New;
            var expected = undo ? g[^1].New : g[0].Old;
            var current = log.ReadCurrent(id);
            if (!MatchesExpected(current, expected))
            {
                log.ClearHistory();
                Reevaluate();
                return new StepOutcome(HistoryText.HistoryCut, null, null, []);
            }
            items.Add((id, target, current, expected));
        }

        // Pass 2: decide what to write, leaving out what was deleted since.
        var writes = new List<(string SectionId, SectionRow? Row)>();
        var sentences = new List<string>();
        string? select = null;
        SectionRow? shownRow = null;
        foreach (var (id, target, current, expected) in items)
        {
            shownRow ??= target ?? expected ?? current;
            var row = DecideTarget(target, sentences);
            if (row is not null) select ??= id;
            if (!SameRow(row, current)) writes.Add((id, row));
        }

        if (writes.Count > 0)
        {
            try
            {
                _db.MarkDirty();   // counts the edit, writes the marker, refuses when read-only
                log.WriteRows(step.Id, writes, markUndone: undo);
            }
            catch (WriteAccessLostException ex)
            {
                return new StepOutcome(ex.Message, null, null, []);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SectionHistory.Apply");
                return new StepOutcome(WriteFailedMessage, null, null, []);
            }
        }
        else
        {
            try { log.WriteRows(step.Id, [], markUndone: undo); }   // only moves the step between the lists
            catch (Exception ex)
            {
                _logger.LogError(ex, "SectionHistory.Apply");
                return new StepOutcome(WriteFailedMessage, null, null, []);
            }
        }

        _store.Reload(_sectionRepo, _semesters.SelectedSemesters.Select(s => s.Semester.Id));

        // Nothing left to save: re-arm the dirty signal, so the very next write shows "unsaved" again.
        Refresh();
        if (!LogHasUnsaved()) _db.ResetDirty();
        Reevaluate();

        return new StepOutcome(null, select, NotShownSemesterOf(shownRow), sentences);
    }

    /// <summary>
    /// Decides the row to write for a step's target: null deletes (or keeps deleted) a section whose course or semester
    /// was deleted this session, or that was removed by Empty Semester (each with a sentence); a row that refers to things deleted this session is rewritten without
    /// them (with a sentence naming them); any other row is written exactly as logged.
    /// </summary>
    /// <param name="target">The logged row to go back to; null means the section should not exist.</param>
    /// <param name="sentences">Receives the sentences about what was not put back.</param>
    /// <returns>The row to write; null for none.</returns>
    private SectionRow? DecideTarget(SectionRow? target, List<string> sentences)
    {
        if (target is null) return null;
        var log = _log!;

        if (target.CourseId is { } courseId && log.WasDeleted("Courses", courseId))
        {
            var name = HistoryText.CourseCodeOf(log.DeletedData("Courses", courseId)) ?? target.CourseCode ?? "";
            sentences.Add(HistoryText.SectionNotPutBack(HistoryText.Heading(target), "course", name));
            return null;
        }
        if (log.WasDeleted("Semesters", target.SemesterId))
        {
            var name = HistoryText.SemesterNameOf(log.DeletedData("Semesters", target.SemesterId)) ?? target.SemesterName ?? "";
            sentences.Add(HistoryText.SectionNotPutBack(HistoryText.Heading(target), "semester", name));
            return null;
        }
        if (log.WasDeleted("Sections", target.Id))
        {
            sentences.Add(HistoryText.SectionNotPutBackEmptied(HistoryText.Heading(target), target.SemesterName ?? ""));
            return null;
        }

        var section = JsonHelpers.Deserialize<Section>(target.Data);
        var removed = SectionReferences.RemoveDeleted(section, log.WasDeleted);
        if (removed.Count == 0) return target;

        var things = removed.DistinctBy(r => (r.Table, r.Id))
            .Select(r => (r.Kind, Name: HistoryText.NameOf(r, log.DeletedData(r.Table, r.Id))))
            .ToList();
        sentences.Add(HistoryText.DeletedSince(things));
        return target with { Data = JsonHelpers.Serialize(section) };
    }

    /// <summary>
    /// Whether a section's current row is what the step says it should be. True when both are absent; when they are equal
    /// (ignoring the readable columns); when the current row equals the expected one after the deletes' own cleanup
    /// (<see cref="SectionReferences.RemoveDeleted"/> applied to the expected row); or when the section is gone because
    /// its course or semester was deleted this session, or it was removed by a paused delete (Empty Semester).
    /// </summary>
    /// <param name="current">The row in the database now; null when absent.</param>
    /// <param name="expected">The row the step left (undo) or started from (redo); null when it should be absent.</param>
    /// <returns>True when the step may be applied to this section.</returns>
    private bool MatchesExpected(SectionRow? current, SectionRow? expected)
    {
        if (current is null && expected is null) return true;
        if (expected is null) return false;
        if (current is null)
            return (expected.CourseId is { } c && _log!.WasDeleted("Courses", c)) || _log!.WasDeleted("Semesters", expected.SemesterId)
                || _log!.WasDeleted("Sections", expected.Id);
        if (SameRow(current, expected)) return true;

        var section = JsonHelpers.Deserialize<Section>(expected.Data);
        if (SectionReferences.RemoveDeleted(section, _log!.WasDeleted).Count == 0) return false;
        return SameRow(current, expected with { Data = JsonHelpers.Serialize(section) });
    }

    /// <summary>
    /// Compares two rows on Id, SemesterId, CourseId, RoomId, SectionCode and Data (the data as JSON, so key order does
    /// not matter), ignoring the three readable columns, which are recomputed whenever a row is written.
    /// </summary>
    /// <param name="a">One row, or null.</param>
    /// <param name="b">The other row, or null.</param>
    /// <returns>True when both are null or both are equal in those columns.</returns>
    private static bool SameRow(SectionRow? a, SectionRow? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a.Id != b.Id || a.SemesterId != b.SemesterId || a.CourseId != b.CourseId
            || a.RoomId != b.RoomId || a.SectionCode != b.SectionCode)
            return false;
        if (a.Data == b.Data) return true;
        try { return JsonNode.DeepEquals(JsonNode.Parse(a.Data), JsonNode.Parse(b.Data)); }
        catch (System.Text.Json.JsonException) { return false; }
    }

    /// <summary>
    /// The name of the row's semester when it is not among the shown semesters, for the "(Winter 2026, not shown)" note.
    /// </summary>
    /// <param name="row">A row of the step's first section, or null.</param>
    /// <returns>The semester's name; null when it is shown, deleted, unknown, or there is no row.</returns>
    private string? NotShownSemesterOf(SectionRow? row)
    {
        if (row is null || _semesters.SelectedSemesters.Any(s => s.Semester.Id == row.SemesterId)) return null;

        // A semester that no longer exists is not "not shown": the section-not-put-back sentence says it is gone.
        if (_log!.WasDeleted("Semesters", row.SemesterId)) return null;
        return string.IsNullOrEmpty(row.SemesterName) ? null : row.SemesterName;
    }

    // ── Unsaved state and events ─────────────────────────────────────────────

    /// <summary>True when the log alone says something is unsaved: a step not undone, other changes, or an incomplete history.</summary>
    private bool LogHasUnsaved() =>
        _steps.Any(s => !s.IsUndone) || _log!.OtherChanges || _log.HistoryIncomplete;

    /// <summary>Re-reads the steps and the incomplete flag from the log (empty when it is unavailable).</summary>
    private void Refresh()
    {
        _steps = IsAvailable ? _log!.GetSteps() : [];
        _historyIncomplete = IsAvailable && _log!.HistoryIncomplete;
    }

    /// <summary>
    /// Re-reads the log, recomputes <see cref="HasUnsavedChanges"/> and <see cref="UnsavedSince"/>, and raises
    /// <see cref="Changed"/>.
    /// </summary>
    private void Reevaluate()
    {
        Refresh();
        var unsaved = IsAvailable ? _dirty && LogHasUnsaved() : _dirty;
        if (unsaved && !_hasUnsaved) _unsavedSince = DateTime.Now;
        if (!unsaved) _unsavedSince = null;
        _hasUnsaved = unsaved;
        Changed?.Invoke();
    }

    /// <summary>Logs the log's install failure once per failure (the data layer has no logger).</summary>
    private void ReportInstallError()
    {
        if (_log?.InstallError is not { } error || ReferenceEquals(error, _loggedInstallError)) return;
        _loggedInstallError = error;
        _logger.LogError(error, "SectionHistory: the undo log could not be created; this session has no undo.");
    }

    /// <summary>The first write after a checkout or save (or an edit that arrived during a save): unsaved.</summary>
    private void OnBecameDirty()
    {
        _dirty = true;
        Reevaluate();
    }

    /// <summary>A save worked: nothing is unsaved until <see cref="OnBecameDirty"/> says otherwise.</summary>
    private void OnSaveCompleted()
    {
        _dirty = false;
        Reevaluate();
    }

    /// <summary>
    /// A save succeeded. When it kept no marker, steps made before its snapshot are in the saved file: drop them (and the
    /// flags, if nothing was edited since) and move <see cref="LastSaveTime"/>. When it kept the marker, trim nothing: the
    /// next save does.
    /// </summary>
    /// <param name="editCountAtSnapshot">The edit count read before the save's snapshot.</param>
    /// <param name="keptMarker">True when an edit arrived during the save.</param>
    private void OnSaveSucceeded(long editCountAtSnapshot, bool keptMarker)
    {
        if (!keptMarker)
        {
            LastSaveTime = DateTime.Now;
            SavedThisSession = true;
            var editedSince = (_signals?.EditGeneration ?? editCountAtSnapshot) != editCountAtSnapshot;
            if (IsAvailable) _log!.TrimAfterSave(editCountAtSnapshot, resetFlags: !editedSince);

            // An edit after the save's last look but before this event is unsaved, whatever order the events arrived in.
            if (editedSince) _dirty = true;
        }
        Reevaluate();
    }

    /// <summary>The connection was reopened: the history is empty, or gone when the session became read-only.</summary>
    private void OnLogReset()
    {
        ReportInstallError();
        if (_dirty && IsAvailable) _log!.MarkHistoryIncomplete();   // unsaved changes survive the reopen, as one lump
        Reevaluate();
    }

    /// <summary>Ends the pause when disposed and re-evaluates the unsaved state (the paused writes were changes).</summary>
    private sealed class ActivePause : IDisposable
    {
        private SectionHistory? _owner;

        /// <summary>Creates the handle for an active pause.</summary>
        /// <param name="owner">The history that started it.</param>
        public ActivePause(SectionHistory owner) => _owner = owner;

        /// <summary>Ends the pause (once).</summary>
        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null) return;
            owner._log!.EndPause();
            owner.Reevaluate();
        }
    }

    /// <summary>The handle returned when there is nothing to pause.</summary>
    private sealed class NoPause : IDisposable
    {
        /// <summary>The shared instance.</summary>
        public static readonly NoPause Instance = new();

        /// <summary>Does nothing.</summary>
        public void Dispose() { }
    }
}
