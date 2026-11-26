using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using TermPoint.Behaviors;
using TermPoint.Services;

namespace TermPoint.ViewModels;

/// <summary>
/// The Undo and Redo buttons beside Save, the history list under Undo's arrow, and the line that says what an undo did
/// (undo design, <c>designs/1.2.5/undo.md</c>, stories 1–3). A thin layer over <see cref="SectionHistory"/>: every rule
/// (what can be undone, the open-card refusal, what is left out) lives there; this class only shows its state and turns
/// clicks and keys into its calls.
/// <para>Buttons are disabled when there is nothing to do, but the keys (Ctrl+Z, Ctrl+Y) always call through, so a
/// press with nothing to undo is answered with "Nothing to undo since your last save" instead of nothing.</para>
/// <para>The result line stays until the history next changes for another reason (a new step, a save) or until
/// <see cref="ResultLineDuration"/> passes, whichever is first. Its own undo's change events do not clear it.</para>
/// </summary>
public partial class UndoBarViewModel : ViewModelBase
{
    /// <summary>How long the line saying what an undo did stays beside Save.</summary>
    public static readonly TimeSpan ResultLineDuration = TimeSpan.FromSeconds(15);

    private readonly SectionHistory _history;
    private readonly SectionStore _store;
    private readonly Func<TimeSpan, Action, IDisposable> _schedule;

    /// <summary>True while this class's own undo or redo runs, so the history's change events don't clear the result line.</summary>
    private bool _applying;

    /// <summary>The pending clear of the result line; null when none.</summary>
    private IDisposable? _resultTimer;

    /// <summary>
    /// Creates the bar over a history and follows its changes.
    /// </summary>
    /// <param name="history">The open database's undo history.</param>
    /// <param name="store">The section store, used to select the section an undo touched (when it is shown).</param>
    /// <param name="schedule">
    /// Runs an action once after a delay and returns a handle that cancels it; null for a UI-thread timer. Tests pass
    /// their own.
    /// </param>
    public UndoBarViewModel(SectionHistory history, SectionStore store, Func<TimeSpan, Action, IDisposable>? schedule = null)
    {
        _history = history;
        _store = store;
        _schedule = schedule ?? ScheduleOnUiThread;
        _history.Changed += OnHistoryChanged;
        Refresh();
    }

    // ── State shown ──────────────────────────────────────────────────────────

    /// <summary>True when the history works for this session (a writer session of the desktop build): the buttons show.</summary>
    [ObservableProperty] private bool _isAvailable;

    /// <summary>True when there is a step to undo: Undo and its arrow are enabled.</summary>
    [ObservableProperty] private bool _canUndo;

    /// <summary>True when there is a step to redo: Redo is enabled.</summary>
    [ObservableProperty] private bool _canRedo;

    /// <summary>Undo's tooltip: "Undo: … (Ctrl+Z)", or "Nothing to undo since your last save".</summary>
    [ObservableProperty] private string _undoTooltip = HistoryText.NothingToUndo;

    /// <summary>Redo's tooltip: "Redo: … (Ctrl+Y)", or "Nothing to redo".</summary>
    [ObservableProperty] private string _redoTooltip = HistoryText.NothingToRedo;

    /// <summary>The line saying what the last undo or redo did, or why it was refused; null when none is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowResultLine))]
    private string? _resultLine;

    /// <summary>True while <see cref="ResultLine"/> is showing.</summary>
    public bool ShowResultLine => !string.IsNullOrEmpty(ResultLine);

    /// <summary>
    /// The history list, newest first: one line per undoable step, then the last line ("Your last save · 10:42", or
    /// "Earlier changes can't be undone one by one" after a cut). Rebuilt whenever the history changes.
    /// </summary>
    public ObservableCollection<HistoryLineViewModel> Lines { get; } = new();

    // ── Commands ─────────────────────────────────────────────────────────────

    /// <summary>Undoes the newest step (the Undo button and Ctrl+Z).</summary>
    [RelayCommand]
    private void Undo() => Show(() => _history.Undo());

    /// <summary>Redoes the step undone last (the Redo button and Ctrl+Y).</summary>
    [RelayCommand]
    private void Redo() => Show(() => _history.Redo());

    /// <summary>
    /// Undoes a line of the history list and every newer one; the last line undoes everything back to the last save.
    /// </summary>
    /// <param name="line">The clicked line.</param>
    [RelayCommand]
    private void UndoTo(HistoryLineViewModel? line)
    {
        if (line is null) return;
        Show(() => line.StepId is { } id ? _history.UndoTo(id) : _history.UndoAll());
    }

    /// <summary>
    /// Runs one history call, then shows its message and selects the section it touched when that section is shown.
    /// </summary>
    /// <param name="call">The history call.</param>
    private void Show(Func<HistoryResult> call)
    {
        HistoryResult result;
        _applying = true;
        try { result = call(); }
        finally { _applying = false; }

        if (result.SectionIdToSelect is { } id && _store.Sections.Any(s => s.Id == id))
            _store.SetSelection(id);

        SetResultLine(result.Message);
    }

    // ── Following the history ────────────────────────────────────────────────

    /// <summary>Refreshes the shown state; a change not caused by this class's own call also clears the result line.</summary>
    private void OnHistoryChanged()
    {
        if (_history.IsDisposed)
        {
            _history.Changed -= OnHistoryChanged;
            SetResultLine(null);
            IsAvailable = false;
            return;
        }
        if (!_applying) SetResultLine(null);
        Refresh();
    }

    /// <summary>Reads the history into the buttons, tooltips and list.</summary>
    private void Refresh()
    {
        IsAvailable = _history.IsAvailable;
        CanUndo = _history.CanUndo;
        CanRedo = _history.CanRedo;
        UndoTooltip = _history.NextUndoLabel is { } u ? HistoryText.UndoTooltip(u) : HistoryText.NothingToUndo;
        RedoTooltip = _history.NextRedoLabel is { } r ? HistoryText.RedoTooltip(r) : HistoryText.NothingToRedo;

        Lines.Clear();
        foreach (var step in _history.UndoableSteps)
            Lines.Add(new HistoryLineViewModel(this, step.Id, HistoryText.StepLine(step.Label, step.At)));
        Lines.Add(new HistoryLineViewModel(this, null, _history.HistoryReachesLastSave
            ? HistoryText.LastSave(_history.LastSaveTime)
            : HistoryText.EarlierChangesCantBeUndoneOneByOne));
    }

    /// <summary>
    /// Highlights the hovered line and every newer one (the lines above it), which are the ones a click would undo.
    /// </summary>
    /// <param name="hovered">The line under the pointer, or null when none is.</param>
    internal void HighlightFrom(HistoryLineViewModel? hovered)
    {
        var index = hovered is null ? -1 : Lines.IndexOf(hovered);
        for (var i = 0; i < Lines.Count; i++)
            Lines[i].IsHighlighted = i <= index;
    }

    /// <summary>Shows (or clears) the result line, restarting its timer.</summary>
    /// <param name="text">The line, or null to clear it.</param>
    private void SetResultLine(string? text)
    {
        _resultTimer?.Dispose();
        _resultTimer = null;
        ResultLine = text;
        if (!string.IsNullOrEmpty(text))
            _resultTimer = _schedule(ResultLineDuration, () => { _resultTimer = null; ResultLine = null; });
    }

    /// <summary>The default scheduler: a one-shot UI-thread timer, stopped when the handle is disposed.</summary>
    /// <param name="delay">The delay.</param>
    /// <param name="action">What to run.</param>
    /// <returns>A handle that stops the timer.</returns>
    private static IDisposable ScheduleOnUiThread(TimeSpan delay, Action action)
    {
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) => { timer.Stop(); action(); };
        timer.Start();
        return new TimerHandle(timer);
    }

    /// <summary>Stops a <see cref="DispatcherTimer"/> when disposed.</summary>
    /// <param name="timer">The timer.</param>
    private sealed class TimerHandle(DispatcherTimer timer) : IDisposable
    {
        /// <summary>Stops the timer.</summary>
        public void Dispose() => timer.Stop();
    }
}

/// <summary>
/// One line of the history list: a step (undoes it and every newer step) or the last line (undoes everything back to
/// the last save). Pointing at it highlights it and every newer line, through <see cref="HoverTracking"/>.
/// </summary>
public partial class HistoryLineViewModel : ObservableObject, IHoverTarget
{
    private readonly UndoBarViewModel _owner;

    /// <summary>Creates a line.</summary>
    /// <param name="owner">The bar that owns the list (it highlights the lines and runs the undo).</param>
    /// <param name="stepId">The step's id; null for the last line.</param>
    /// <param name="text">The line's text.</param>
    internal HistoryLineViewModel(UndoBarViewModel owner, long? stepId, string text)
    {
        _owner = owner;
        StepId = stepId;
        Text = text;
    }

    /// <summary>The step this line undoes back to; null for the last line, which undoes everything.</summary>
    public long? StepId { get; }

    /// <summary>The line's text: "Move … · 10:58", or the last line's.</summary>
    public string Text { get; }

    /// <summary>The hover text: "These will be undone".</summary>
    public string Tooltip => HistoryText.TheseWillBeUndone;

    /// <summary>True when a click on the hovered line would undo this one.</summary>
    [ObservableProperty] private bool _isHighlighted;

    /// <summary>The line's command: undo back to here.</summary>
    public IRelayCommand<HistoryLineViewModel?> Command => _owner.UndoToCommand;

    /// <inheritdoc/>
    public void OnHoverChanged(bool isHovered)
    {
        if (isHovered) _owner.HighlightFrom(this);
        else if (IsHighlighted) _owner.HighlightFrom(null);
    }
}
