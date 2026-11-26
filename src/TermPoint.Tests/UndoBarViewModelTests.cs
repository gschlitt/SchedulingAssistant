using Microsoft.Data.Sqlite;
using Moq;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests of <see cref="UndoBarViewModel"/> (the Undo and Redo buttons, the history list and the result line) over a real
/// <see cref="SectionHistory"/> on a temp database. The rules themselves are tested in <see cref="SectionHistoryTests"/>;
/// these check only that the bar shows the history's state and turns clicks into its calls.
/// </summary>
public sealed class UndoBarViewModelTests : IDisposable
{
    private readonly string _dir;
    private readonly SectionHistoryTests.FakeSignals _signals = new();
    private readonly Card _card = new();
    private readonly DatabaseContext _ctx;
    private readonly SectionRepository _sections;
    private readonly SectionStore _store = new();
    private readonly SemesterContext _semesters = new();
    private readonly SectionHistory _history;
    private readonly List<(TimeSpan Delay, Action Action, Handle Handle)> _scheduled = new();
    private readonly UndoBarViewModel _bar;

    /// <summary>One academic year, Fall 2026 shown, the course HIST 101, a clean history, and the bar over it.</summary>
    public UndoBarViewModelTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"undobar_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _ctx = new DatabaseContext(Path.Combine(_dir, "wc.db"),
            onFirstWrite: () => _signals.RaiseBecameDirty(),
            onEveryWrite: () => _signals.EditGeneration++);
        _sections = new SectionRepository(_ctx);
        _history = new SectionHistory(_ctx, _store, _sections, _semesters, _card, _signals, new Mock<IAppLogger>().Object);

        var years = new AcademicYearRepository(_ctx);
        var sems = new SemesterRepository(_ctx);
        years.Insert(new AcademicYear { Id = "ay1", Name = "2026-2027" });
        sems.Insert(new Semester { Id = "sem1", AcademicYearId = "ay1", Name = "Fall 2026", SortOrder = 1 });
        using (var cmd = ((SqliteConnection)_ctx.Connection).CreateCommand())
        {
            cmd.CommandText = "INSERT INTO Courses(id, subject_id, calendar_code, title, data) VALUES ('c1', 's', 'HIST 101', 't', '{\"calendarCode\":\"HIST 101\"}')";
            cmd.ExecuteNonQuery();
        }
        _semesters.Reload(years, sems, "ay1", new HashSet<string> { "sem1" });
        Save();

        _bar = new UndoBarViewModel(_history, _store, (delay, action) =>
        {
            var handle = new Handle();
            _scheduled.Add((delay, action, handle));
            return handle;
        });
    }

    /// <summary>Closes the context and deletes the folder.</summary>
    public void Dispose()
    {
        _history.Dispose();
        _ctx.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>With nothing to undo: buttons disabled, the "nothing" tooltips, and a list holding only the last-save line.</summary>
    [Fact]
    public void Clean_ShowsNothingToDo()
    {
        Assert.True(_bar.IsAvailable);
        Assert.False(_bar.CanUndo);
        Assert.False(_bar.CanRedo);
        Assert.Equal(HistoryText.NothingToUndo, _bar.UndoTooltip);
        Assert.Equal(HistoryText.NothingToRedo, _bar.RedoTooltip);
        var last = Assert.Single(_bar.Lines);
        Assert.Null(last.StepId);
        Assert.StartsWith("Your last save · ", last.Text);
    }

    /// <summary>Steps show newest first with the last-save line at the end; the tooltip names the newest step.</summary>
    [Fact]
    public void Steps_ShowNewestFirst_AndNameTheNextUndo()
    {
        var a = Add("A");
        Step("first", () => { a.Notes = "1"; _sections.Update(a); });
        Step("second", () => { a.Notes = "2"; _sections.Update(a); });

        Assert.True(_bar.CanUndo);
        Assert.Equal(HistoryText.UndoTooltip("second"), _bar.UndoTooltip);
        Assert.Equal(3, _bar.Lines.Count);
        Assert.StartsWith("second · ", _bar.Lines[0].Text);
        Assert.StartsWith("first · ", _bar.Lines[1].Text);
        Assert.Null(_bar.Lines[2].StepId);
    }

    /// <summary>Undo undoes, shows "Undone: …", enables Redo and selects the section; Redo puts it back.</summary>
    [Fact]
    public void UndoAndRedo_ShowTheResult_AndSelectTheSection()
    {
        var a = Add("A");
        Step("edit A", () => { a.Notes = "x"; _sections.Update(a); });

        _bar.UndoCommand.Execute(null);

        Assert.Equal("Undone: edit A", _bar.ResultLine);
        Assert.True(_bar.ShowResultLine);
        Assert.False(_bar.CanUndo);
        Assert.True(_bar.CanRedo);
        Assert.Equal(HistoryText.RedoTooltip("edit A"), _bar.RedoTooltip);
        Assert.Contains(a.Id, _store.SelectedSectionIds);

        _bar.RedoCommand.Execute(null);
        Assert.StartsWith("Redone: edit A", _bar.ResultLine);
        Assert.True(_bar.CanUndo);
    }

    /// <summary>A press with nothing to undo says so instead of doing nothing.</summary>
    [Fact]
    public void Undo_WithNothing_SaysSo()
    {
        _bar.UndoCommand.Execute(null);
        Assert.Equal(HistoryText.NothingToUndo, _bar.ResultLine);
    }

    /// <summary>An open card refuses the undo with the card message and changes nothing.</summary>
    [Fact]
    public void Undo_WithACardOpen_IsRefused()
    {
        var a = Add("A");
        Step("edit A", () => { a.Notes = "x"; _sections.Update(a); });
        _card.OpenCardHeading = "HIST 101 A";

        _bar.UndoCommand.Execute(null);

        Assert.Equal(OpenCardMessages.WhyRefused(_card), _bar.ResultLine);
        Assert.True(_bar.CanUndo);
    }

    /// <summary>A line undoes it and every newer step; the last line undoes everything.</summary>
    [Fact]
    public void Lines_UndoBackToThemselves()
    {
        var a = Add("A");
        Step("first", () => { a.Notes = "1"; _sections.Update(a); });
        Step("second", () => { a.Notes = "2"; _sections.Update(a); });
        Step("third", () => { a.Notes = "3"; _sections.Update(a); });

        var second = _bar.Lines[1];
        second.Command.Execute(second);
        Assert.Equal(new[] { "first" }, _history.UndoableSteps.Select(s => s.Label).ToArray());

        var last = _bar.Lines[^1];
        last.Command.Execute(last);
        Assert.False(_history.CanUndo);
        Assert.Single(_bar.Lines);
    }

    /// <summary>Pointing at a line highlights it and every newer line; leaving clears the highlight.</summary>
    [Fact]
    public void Hover_HighlightsTheLineAndNewerOnes()
    {
        var a = Add("A");
        Step("first", () => { a.Notes = "1"; _sections.Update(a); });
        Step("second", () => { a.Notes = "2"; _sections.Update(a); });

        _bar.Lines[1].OnHoverChanged(true);
        Assert.Equal(new[] { true, true, false }, _bar.Lines.Select(l => l.IsHighlighted).ToArray());

        _bar.Lines[1].OnHoverChanged(false);
        Assert.All(_bar.Lines, l => Assert.False(l.IsHighlighted));
    }

    /// <summary>The result line clears when its timer fires, and when the history changes for another reason.</summary>
    [Fact]
    public void ResultLine_ClearsOnTimerOrNextChange()
    {
        var a = Add("A");
        Step("first", () => { a.Notes = "1"; _sections.Update(a); });
        Step("second", () => { a.Notes = "2"; _sections.Update(a); });

        _bar.UndoCommand.Execute(null);
        Assert.NotNull(_bar.ResultLine);
        var (delay, action, _) = _scheduled[^1];
        Assert.Equal(UndoBarViewModel.ResultLineDuration, delay);
        action();
        Assert.Null(_bar.ResultLine);

        _bar.UndoCommand.Execute(null);
        Assert.NotNull(_bar.ResultLine);
        Step("new", () => { a.Notes = "n"; _sections.Update(a); });
        Assert.Null(_bar.ResultLine);
        Assert.True(_scheduled[^1].Handle.Disposed);
    }

    /// <summary>When the history is disposed (the database closes) the bar hides.</summary>
    [Fact]
    public void HistoryDisposed_HidesTheBar()
    {
        _history.Dispose();
        Assert.False(_bar.IsAvailable);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Inserts a Fall 2026 HIST 101 section as setup (then a save, so it is not a step) and loads the store.</summary>
    private Section Add(string code)
    {
        var s = new Section
        {
            SemesterId = "sem1", CourseId = "c1", SectionCode = code,
            Schedule = { new SectionDaySchedule { Day = 1, StartMinutes = 600, DurationMinutes = 80 } },
        };
        _sections.Insert(s);
        Save();
        _store.Reload(_sections, ["sem1"]);
        return s;
    }

    /// <summary>Records a step the way a caller does.</summary>
    private void Step(string label, Action write) => _history.Record(label, write);

    /// <summary>Simulates a save of everything so far.</summary>
    private void Save()
    {
        _signals.Save(_history);
        _ctx.ResetDirty();
    }

    /// <summary>A card state that can be opened by hand.</summary>
    private sealed class Card : ISectionCardState
    {
        /// <inheritdoc/>
        public string? OpenCardHeading { get; set; }
        /// <inheritdoc/>
        public event Action? OpenCardChanged { add { } remove { } }
    }

    /// <summary>A scheduled action's handle, remembering whether it was cancelled.</summary>
    private sealed class Handle : IDisposable
    {
        /// <summary>True once disposed.</summary>
        public bool Disposed { get; private set; }
        /// <inheritdoc/>
        public void Dispose() => Disposed = true;
    }
}
