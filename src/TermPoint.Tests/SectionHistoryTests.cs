using Microsoft.Data.Sqlite;
using Moq;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests of <see cref="SectionHistory"/> (the 1.2.5 undo service) over the REAL <see cref="DatabaseContext"/>, its change
/// log and the real repositories on a temp file. The checkout service is replaced by <see cref="FakeSignals"/>, wired the
/// way production wires it: the context's first-write callback raises <c>BecameDirty</c> and its every-write callback
/// counts edits. See <c>designs/1.2.5/undo.md</c>.
/// </summary>
public sealed class SectionHistoryTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;
    private readonly FakeSignals _signals = new();
    private readonly FakeCard _card = new();
    private readonly RecordingLogger _logger = new();
    private readonly DatabaseContext _ctx;
    private readonly SectionRepository _sections;
    private readonly SectionStore _store = new();
    private readonly SemesterContext _semesters = new();
    private readonly SectionHistory _history;
    private int _changed;

    /// <summary>Number of times the context's first-write callback ran (the dirty marker would be written).</summary>
    private int FirstWrites { get; set; }

    /// <summary>
    /// Creates a database with one academic year, two semesters (Fall 2026 shown, Winter 2026 not shown), the course
    /// HIST 101, and the history over it. Setup writes end with a simulated save, so every test starts clean.
    /// </summary>
    public SectionHistoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"sectionhistory_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "wc.db");
        _ctx = new DatabaseContext(_path,
            onFirstWrite: () => { FirstWrites++; _signals.RaiseBecameDirty(); },
            onEveryWrite: () => _signals.EditGeneration++);
        _sections = new SectionRepository(_ctx);
        _history = new SectionHistory(_ctx, _store, _sections, _semesters, _card, _signals, _logger);
        _history.Changed += () => _changed++;

        var years = new AcademicYearRepository(_ctx);
        var sems = new SemesterRepository(_ctx);
        Seed(() =>
        {
            years.Insert(new AcademicYear { Id = "ay1", Name = "2026-2027" });
            sems.Insert(new Semester { Id = "sem1", AcademicYearId = "ay1", Name = "Fall 2026", SortOrder = 1 });
            sems.Insert(new Semester { Id = "sem2", AcademicYearId = "ay1", Name = "Winter 2026", SortOrder = 2 });
            Raw("INSERT INTO Courses(id, subject_id, calendar_code, title, data) VALUES ('c1', 's', 'HIST 101', 't', '{\"calendarCode\":\"HIST 101\"}')");
        });
        _semesters.Reload(years, sems, "ay1", new HashSet<string> { "sem1" });
    }

    /// <summary>Closes the context and deletes the folder.</summary>
    public void Dispose()
    {
        _history.Dispose();
        _ctx.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // ── Recording ────────────────────────────────────────────────────────────

    /// <summary>One call is one step, however many rows it writes; a no-op update makes no step.</summary>
    [Fact]
    public void Record_MakesOneStepPerCall_AndNoneForANoOpUpdate()
    {
        var a = Seed(Make("A")); var b = Seed(Make("B"));

        _history.Record("two rows", () =>
        {
            a.Notes = "x"; _sections.Update(a);
            b.Notes = "y"; _sections.Update(b);
        });
        Assert.Equal(1, _history.UndoableSteps.Count);
        Assert.Equal("two rows", _history.NextUndoLabel);
        Assert.True(_history.CanUndo);
        Assert.False(_history.CanRedo);

        var changedBefore = _changed;
        _history.Record("nothing changes", () => _sections.Update(a));
        Assert.Single(_history.UndoableSteps);
        Assert.True(_changed > changedBefore);

        var result = _history.Record("returns a value", () => { b.Notes = "z"; _sections.Update(b); return 42; });
        Assert.Equal(42, result);
        Assert.Equal(new[] { "returns a value", "two rows" }, _history.UndoableSteps.Select(s => s.Label).ToArray());
    }

    /// <summary>Exceptions from the write propagate, the step is still ended, and the history stays usable.</summary>
    [Fact]
    public void Record_PropagatesExceptions_AndEndsTheStep()
    {
        var a = Seed(Make("A"));

        Assert.Throws<InvalidOperationException>(() => _history.Record("boom", () =>
        {
            a.Notes = "x"; _sections.Update(a);
            throw new InvalidOperationException("boom");
        }));

        Assert.Single(_history.UndoableSteps);       // the write that happened is a step, as it is in the file
        _history.Record("next", () => { a.Notes = "y"; _sections.Update(a); });
        Assert.Equal(2, _history.UndoableSteps.Count);
    }

    /// <summary>A step started while another is open cannot be recorded: a warning, and the write still runs.</summary>
    [Fact]
    public void Record_WhenTheLogIsBusy_RunsUnrecordedAndWarns()
    {
        var a = Seed(Make("A"));
        var ran = false;

        _history.Record("outer", () =>
        {
            a.Notes = "x"; _sections.Update(a);
            _history.Record("inner", () => ran = true);
        });

        Assert.True(ran);
        Assert.Equal(1, _logger.Warnings.Count);
        Assert.Equal("outer", Assert.Single(_history.UndoableSteps).Label);
    }

    // ── Undo and redo ────────────────────────────────────────────────────────

    /// <summary>
    /// Undo and redo restore exact rows: meetings, an unknown JSON field, and a deleted section re-inserted with its id.
    /// The section store is reloaded and the first surviving section is returned for selection.
    /// </summary>
    [Fact]
    public void UndoAndRedo_RestoreExactRows()
    {
        var a = Seed(Make("A")); var b = Seed(Make("B")); var c = Seed(Make("C"));
        a.Schedule.Add(new SectionDaySchedule { Day = 3, StartMinutes = 780, DurationMinutes = 50, RoomId = "r1" });
        Seed(() => _sections.Update(a));
        Raw($"UPDATE Sections SET data = json_set(data, '$.fromTheFuture', json('{{\"k\":[1,2]}}')) WHERE id = '{b.Id}'");
        Seed(() => { });
        _store.Reload(_sections, new[] { "sem1" });
        var before = All();

        var created = Make("NEW");
        _history.Record("Edit A", () => { a.Schedule[0].Day = 2; _sections.Update(a); });
        _history.Record("Delete B", () => _sections.Delete(b.Id));
        _history.Record("Add NEW", () => _sections.Insert(created));
        _history.Record("Edit C", () => { c.Notes = "edited"; _sections.Update(c); });
        var after = All();

        var undone = _history.Undo();
        Assert.True(undone.Done);
        Assert.Equal("Undone: Edit C", undone.Message);
        Assert.Equal(c.Id, undone.SectionIdToSelect);
        Assert.Contains(_store.Sections, s => s.Id == c.Id && s.Notes == "");

        Assert.True(_history.UndoAll().Done);
        Assert.Equal(before, All());
        Assert.Contains("fromTheFuture", All()[b.Id].Data);
        Assert.Contains(_store.Sections, s => s.Id == b.Id);
        Assert.DoesNotContain(_store.Sections, s => s.Id == created.Id);
        Assert.False(_history.CanUndo);
        Assert.True(_history.CanRedo);
        Assert.Equal("Edit A", _history.NextRedoLabel);

        for (var i = 0; i < 4; i++) Assert.True(_history.Redo().Done);
        Assert.Equal(after, All());
        Assert.False(_history.CanRedo);
        Assert.True(_history.CanUndo);
        Assert.Equal("Edit C", _history.NextUndoLabel);

        Assert.False(_history.Redo().Done);
        Assert.Equal(HistoryText.NothingToRedo, _history.Redo().Message);
    }

    /// <summary>A new step clears the redo list.</summary>
    [Fact]
    public void ANewStep_ClearsTheRedoList()
    {
        var a = Seed(Make("A"));
        _history.Record("one", () => { a.Notes = "1"; _sections.Update(a); });
        _history.Record("two", () => { a.Notes = "2"; _sections.Update(a); });
        _history.Undo();
        Assert.True(_history.CanRedo);

        _history.Record("three", () => { a.Notes = "3"; _sections.Update(a); });

        Assert.False(_history.CanRedo);
        Assert.Equal(new[] { "three", "one" }, _history.UndoableSteps.Select(s => s.Label).ToArray());
    }

    /// <summary>UndoTo undoes the chosen step and every newer one, newest first; UndoAll undoes the rest.</summary>
    [Fact]
    public void UndoTo_AndUndoAll()
    {
        var a = Seed(Make("A"));
        for (var i = 1; i <= 4; i++)
        {
            var n = i;
            _history.Record($"step {n}", () => { a.SectionCode = $"A{n}"; _sections.Update(a); });
        }
        var steps = _history.UndoableSteps;           // newest first
        Assert.Equal("step 4", steps[0].Label);

        var result = _history.UndoTo(steps[2].Id);    // step 2

        Assert.True(result.Done);
        Assert.Equal("A1", Row(a.Id).SectionCode);
        Assert.Equal(new[] { "step 1" }, _history.UndoableSteps.Select(s => s.Label).ToArray());
        Assert.Equal("step 2", _history.NextRedoLabel);
        Assert.StartsWith("Undone: step 2", result.Message);

        Assert.False(_history.UndoTo(steps[0].Id).Done);   // already undone

        Assert.True(_history.UndoAll().Done);
        Assert.Equal("A", Row(a.Id).SectionCode);
        Assert.False(_history.CanUndo);
        Assert.False(_history.UndoAll().Done);
    }

    /// <summary>With a card open, undo is refused with the grid's message and nothing is touched; it works once the card closes.</summary>
    [Fact]
    public void ACardOpen_RefusesUndo_AndTouchesNothing()
    {
        var a = Seed(Make("A"));
        _history.Record("Edit A", () => { a.Notes = "x"; _sections.Update(a); });
        var rows = All();
        var reloads = 0;
        _store.SectionsChanged += () => reloads++;
        _card.OpenCardHeading = "HIST 101 A";

        var undo = _history.Undo();
        var all = _history.UndoAll();

        Assert.False(undo.Done);
        Assert.Equal("Finish editing HIST 101 A in the Section List first.", undo.Message);
        Assert.False(all.Done);
        Assert.Equal(rows, All());
        Assert.Equal(0, reloads);
        Assert.True(_history.CanUndo);

        _card.OpenCardHeading = null;
        Assert.True(_history.Undo().Done);
        Assert.Equal(1, reloads);
    }

    /// <summary>A step in a semester that is not shown works and says so.</summary>
    [Fact]
    public void AHiddenSemester_IsUndoneAndSaysSo()
    {
        var w = Seed(Make("A", semesterId: "sem2"));
        _history.Record("Delete HIST 101 A", () => _sections.Delete(w.Id));

        var result = _history.Undo();

        Assert.True(result.Done);
        Assert.Equal("Undone: Delete HIST 101 A (Winter 2026, not shown)", result.Message);
        Assert.Equal(w.Id, result.SectionIdToSelect);
        Assert.NotNull(_sections.GetById(w.Id));
    }

    // ── Things deleted since ─────────────────────────────────────────────────

    /// <summary>
    /// Story 7: the tag is removed from a section (a step), then the tag is deleted (its cleanup runs paused). Undoing the
    /// step puts the section back without the tag, and the message names it.
    /// </summary>
    [Fact]
    public void ADeletedTag_IsLeftOut_AndNamed()
    {
        Raw("INSERT INTO SchedulingEnvironmentValues(id, type, name, data) VALUES ('t1', 'tag', 'Lab', '{\"id\":\"t1\",\"name\":\"Lab\"}')");
        var a = Make("A"); a.TagIds.Add("t1"); a.TagIds.Add("t2");
        Seed(a);
        _history.Record("Change tags of HIST 101 A", () => { a.TagIds.Remove("t1"); _sections.Update(a); });

        DeleteValue("t1", "tag");

        var result = _history.Undo();

        Assert.True(result.Done);
        Assert.Equal("Undone: Change tags of HIST 101 A. The tag Lab has been deleted, so it wasn't put back.", result.Message);
        Assert.Equal(new[] { "t2" }, _sections.GetById(a.Id)!.TagIds);
    }

    /// <summary>
    /// A meeting dragged out of B 204 (a step), then the room is deleted, which cleans the room off the meetings (paused).
    /// Undoing the drag puts the meeting back without the room; redo and the instructor case follow the same rule.
    /// </summary>
    [Fact]
    public void ADeletedRoomAndInstructor_AreLeftOut_AndNamed()
    {
        new RoomRepository(_ctx).Insert(new Room { Id = "r1", Building = "B", RoomNumber = "204" });
        new InstructorRepository(_ctx).Insert(new Instructor { Id = "i1", FirstName = "Ann", LastName = "Lee", Initials = "AL" });
        var a = Make("A");
        a.Schedule[0].RoomId = "r1";
        a.InstructorAssignments.Add(new InstructorAssignment { InstructorId = "i1" });
        Seed(a);
        _history.Record("Move HIST 101 A Mon 10:00 → Tue 10:00", () => { a.Schedule[0].Day = 2; _sections.Update(a); });
        _history.Record("Change instructors of HIST 101 A", () => { a.InstructorAssignments.Clear(); _sections.Update(a); });

        using (_history.PauseRecording())
        {
            foreach (var s in _sections.GetAll())
                if (SectionReferences.RemoveRoom(s, "r1")) _sections.Update(s);
            new RoomRepository(_ctx).Delete("r1");
        }
        new InstructorRepository(_ctx).Delete("i1");

        // The row each step goes back to still has the room (and the instructor) in it, so both are left out and named.
        var instructors = _history.Undo();
        Assert.Equal("Undone: Change instructors of HIST 101 A. " +
                     "The room B 204 and the instructor Ann Lee have been deleted, so they weren't put back.",
            instructors.Message);
        Assert.Empty(_sections.GetById(a.Id)!.InstructorAssignments);

        var drag = _history.Undo();
        Assert.StartsWith("Undone: Move HIST 101 A Mon 10:00 → Tue 10:00. The room B 204", drag.Message);
        var section = _sections.GetById(a.Id)!;
        Assert.Equal(1, section.Schedule[0].Day);
        Assert.Null(section.Schedule[0].RoomId);

        Assert.True(_history.Redo().Done);   // redo: the same row as the drag left it, room already gone
        Assert.Equal(2, _sections.GetById(a.Id)!.Schedule[0].Day);
    }

    /// <summary>Two things deleted in one step: one plural sentence.</summary>
    [Fact]
    public void SeveralDeletedThings_GiveOnePluralSentence()
    {
        Raw("INSERT INTO SchedulingEnvironmentValues(id, type, name, data) VALUES ('t1', 'tag', 'Lab', '{\"name\":\"Lab\"}')");
        new RoomRepository(_ctx).Insert(new Room { Id = "r1", Building = "B", RoomNumber = "204" });
        var a = Make("A"); a.TagIds.Add("t1"); a.Schedule[0].RoomId = "r1";
        Seed(a);
        _history.Record("Edit HIST 101 A", () => { a.TagIds.Clear(); a.Schedule[0].RoomId = null; _sections.Update(a); });
        DeleteValue("t1", "tag");
        using (_history.PauseRecording()) new RoomRepository(_ctx).Delete("r1");

        var result = _history.Undo();

        Assert.Equal("Undone: Edit HIST 101 A. The room B 204 and the tag Lab have been deleted, so they weren't put back.",
            result.Message);
    }

    /// <summary>
    /// A section whose semester was deleted since stays deleted, for a delete step and for an edit step, and the message
    /// says so; its step still moves to the redo list.
    /// </summary>
    [Fact]
    public void ADeletedSemester_KeepsItsSectionsDeleted()
    {
        var gone = Seed(Make("A", semesterId: "sem2"));
        var edited = Seed(Make("B", semesterId: "sem2"));
        _history.Record("Delete HIST 101 A", () => _sections.Delete(gone.Id));
        _history.Record("Edit HIST 101 B", () => { edited.Notes = "x"; _sections.Update(edited); });
        using (_history.PauseRecording())
        {
            _sections.DeleteBySemesterId("sem2");
            new SemesterRepository(_ctx).Delete("sem2");
        }

        var edit = _history.Undo();
        Assert.True(edit.Done);
        Assert.Equal("Undone: Edit HIST 101 B. HIST 101 B wasn't put back: the semester Winter 2026 has been deleted.", edit.Message);
        Assert.Null(edit.SectionIdToSelect);
        Assert.Null(_sections.GetById(edited.Id));

        var delete = _history.Undo();
        Assert.True(delete.Done);
        Assert.Contains("HIST 101 A wasn't put back: the semester Winter 2026 has been deleted.", delete.Message);
        Assert.Null(_sections.GetById(gone.Id));
        Assert.False(_history.CanUndo);
        Assert.True(_history.CanRedo);
    }

    /// <summary>A section whose course was deleted since stays deleted.</summary>
    [Fact]
    public void ADeletedCourse_KeepsItsSectionDeleted()
    {
        var a = Seed(Make("A"));
        _history.Record("Delete HIST 101 A", () => _sections.Delete(a.Id));
        Raw("DELETE FROM Courses WHERE id = 'c1'");

        var result = _history.Undo();

        Assert.True(result.Done);
        Assert.Contains("HIST 101 A wasn't put back: the course HIST 101 has been deleted.", result.Message);
        Assert.Null(_sections.GetById(a.Id));
    }

    // ── The safety net ───────────────────────────────────────────────────────

    /// <summary>
    /// A section changed another way (a raw write that bypasses everything) cuts the history and refuses with the approved
    /// message; the row is left as it is.
    /// </summary>
    [Fact]
    public void AMismatch_CutsTheHistory()
    {
        var a = Seed(Make("A"));
        _history.Record("Edit HIST 101 A", () => { a.Notes = "x"; _sections.Update(a); });
        using (_history.PauseRecording())
            Raw($"UPDATE Sections SET data = json_set(data, '$.notes', 'changed another way') WHERE id = '{a.Id}'");
        var rows = All();

        var result = _history.Undo();

        Assert.False(result.Done);
        Assert.Equal(HistoryText.HistoryCut, result.Message);
        Assert.Equal(rows, All());
        Assert.False(_history.CanUndo);
        Assert.False(_history.HistoryReachesLastSave);
        Assert.True(_history.HasUnsavedChanges);   // still unsaved
    }

    /// <summary>Paused writes to other sections and writes to other tables never cut the history.</summary>
    [Fact]
    public void PausedWritesAndOtherTables_NeverCutTheHistory()
    {
        var a = Seed(Make("A")); var c = Seed(Make("C"));
        _history.Record("Edit HIST 101 A", () => { a.Notes = "x"; _sections.Update(a); });

        using (_history.PauseRecording())
        {
            c.Notes = "imported"; _sections.Update(c);
            _sections.Insert(Make("D"));
        }
        new RoomRepository(_ctx).Insert(new Room { Building = "B", RoomNumber = "1" });
        Raw("INSERT INTO SchedulingEnvironmentValues(id, type, name, data) VALUES ('v1', 'tag', 'X', '{}')");

        Assert.True(_history.HistoryReachesLastSave);
        Assert.True(_history.CanUndo);
        Assert.True(_history.Undo().Done);
        Assert.Equal("", _sections.GetById(a.Id)!.Notes);
        Assert.Equal("imported", _sections.GetById(c.Id)!.Notes);   // the paused write stays
        Assert.True(_history.HasUnsavedChanges);                    // the paused and other writes are still unsaved
    }

    // ── Unsaved state ────────────────────────────────────────────────────────

    /// <summary>
    /// Record makes the changes unsaved (with a time); undoing everything clears it and re-arms the dirty signal, so a
    /// following instructor edit makes it unsaved again.
    /// </summary>
    [Fact]
    public void UndoingEverything_LeavesNothingUnsaved_AndReArmsTheDirtySignal()
    {
        var a = Seed(Make("A"));
        Assert.False(_history.HasUnsavedChanges);
        Assert.Null(_history.UnsavedSince);

        _history.Record("Edit HIST 101 A", () => { a.Notes = "x"; _sections.Update(a); });
        Assert.True(_history.HasUnsavedChanges);
        Assert.NotNull(_history.UnsavedSince);
        Assert.Equal(1, FirstWrites);

        Assert.True(_history.UndoAll().Done);
        Assert.False(_history.HasUnsavedChanges);
        Assert.Null(_history.UnsavedSince);

        new InstructorRepository(_ctx).Insert(new Instructor { FirstName = "Ann", LastName = "Lee", Initials = "AL" });
        Assert.Equal(2, FirstWrites);                 // ResetDirty re-armed the first-write callback
        _signals.RaiseBecameDirty();                  // production posts it, so it arrives after the write
        Assert.True(_history.HasUnsavedChanges);
        Assert.NotNull(_history.UnsavedSince);

        // A save of that clears it again.
        _signals.Save(_history);
        Assert.False(_history.HasUnsavedChanges);
    }

    /// <summary>A change undo does not cover (an instructor edit) keeps "unsaved" after undoing every step.</summary>
    [Fact]
    public void AnOtherChange_KeepsItUnsaved_AfterUndoAll()
    {
        var a = Seed(Make("A"));
        _history.Record("Edit HIST 101 A", () => { a.Notes = "x"; _sections.Update(a); });
        new InstructorRepository(_ctx).Insert(new Instructor { FirstName = "Ann", LastName = "Lee", Initials = "AL" });

        _history.UndoAll();

        Assert.True(_history.HasUnsavedChanges);
        Assert.Equal(1, FirstWrites);                 // the flag was not reset: something is still unsaved
    }

    /// <summary>
    /// A save that kept no marker drops the steps in it (and the flags if nothing was edited since) and moves the last-save
    /// time; a save that kept the marker trims nothing.
    /// </summary>
    [Fact]
    public void SaveSucceeded_TrimsTheHistory()
    {
        var a = Seed(Make("A"));
        _history.Record("one", () => { a.Notes = "1"; _sections.Update(a); });
        var afterOne = _signals.EditGeneration;
        _history.Record("two", () => { a.Notes = "2"; _sections.Update(a); });
        var started = _history.LastSaveTime;

        // A save whose snapshot was taken after step one; step two came later (the marker was kept).
        _signals.RaiseSaveCompleted();
        _signals.RaiseSaveSucceeded(afterOne, keptMarker: true);
        _signals.RaiseBecameDirty();
        Assert.Equal(2, _history.UndoableSteps.Count);
        Assert.Equal(started, _history.LastSaveTime);
        Assert.True(_history.HasUnsavedChanges);

        // The next save, snapshot after step one but no marker kept, edited since: step one goes, step two stays.
        _signals.RaiseSaveCompleted();
        _signals.RaiseSaveSucceeded(afterOne, keptMarker: false);
        Assert.Equal(new[] { "two" }, _history.UndoableSteps.Select(s => s.Label).ToArray());
        Assert.True(_history.HasUnsavedChanges);       // edited since the snapshot: still unsaved
        Assert.True(_history.LastSaveTime >= started);

        // A save of everything.
        _signals.Save(_history);
        Assert.Empty(_history.UndoableSteps);
        Assert.False(_history.HasUnsavedChanges);
        Assert.False(_history.CanUndo);
        Assert.True(_history.HistoryReachesLastSave);
    }

    /// <summary>A session that opens with a dirty marker (restored changes) is unsaved, and not undoable one by one.</summary>
    [Fact]
    public void ARestoredOpen_IsUnsaved_ButNotUndoable()
    {
        var restored = new FakeSignals { HasDirtyMarker = true };
        using var history = new SectionHistory(_ctx, _store, _sections, _semesters, _card, restored, _logger);

        Assert.True(history.HasUnsavedChanges);
        Assert.False(history.HistoryReachesLastSave);
        Assert.False(history.CanUndo);
        Assert.NotNull(history.UnsavedSince);
    }

    // ── No log ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A read-only session has no log: Record just runs the write, nothing can be undone, and the unsaved state is
    /// today's indicator.
    /// </summary>
    [Fact]
    public void WithoutALog_RecordJustRuns_AndNothingCanBeUndone()
    {
        var roPath = Path.Combine(_dir, "ro.db");
        using (new DatabaseContext(roPath)) { }
        using var readOnly = new DatabaseContext(roPath, readOnly: true);
        var signals = new FakeSignals();
        using var history = new SectionHistory(readOnly, _store, new SectionRepository(readOnly), _semesters, _card, signals, _logger);
        var ran = 0;

        var value = history.Record("x", () => { ran++; return 7; });
        history.Record("y", () => ran++);
        using (history.PauseRecording()) { }

        Assert.Equal((7, 2), (value, ran));
        Assert.False(history.IsAvailable);
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Empty(history.UndoableSteps);
        Assert.False(history.Undo().Done);
        Assert.False(history.UndoAll().Done);
        Assert.False(history.HasUnsavedChanges);
        signals.RaiseBecameDirty();
        Assert.True(history.HasUnsavedChanges);       // today's indicator
        signals.RaiseSaveCompleted();
        Assert.False(history.HasUnsavedChanges);
    }

    /// <summary>A context with no change log at all (the demo) behaves the same.</summary>
    [Fact]
    public void WithAContextThatHasNoLog_ItIsUnavailable()
    {
        var db = new Mock<IDatabaseContext>().Object;
        using var history = new SectionHistory(db, _store, _sections, _semesters, _card, null, _logger);
        var ran = false;

        history.Record("x", () => ran = true);

        Assert.True(ran);
        Assert.False(history.IsAvailable);
        Assert.False(history.CanUndo);
    }

    /// <summary>A refused write (write access lost) leaves the history and the rows alone and says why.</summary>
    [Fact]
    public void WhenWriteAccessIsLost_UndoIsRefused()
    {
        var a = Seed(Make("A"));
        _history.Record("Edit HIST 101 A", () => { a.Notes = "x"; _sections.Update(a); });
        var rows = All();
        _ctx.MarkReadOnly();

        var result = _history.Undo();

        Assert.False(result.Done);
        Assert.Equal(WriteAccessLostException.PlainMessage, result.Message);
        Assert.Equal(rows, All());
        Assert.True(_history.CanUndo);
    }

    // ── The log's lifetime ───────────────────────────────────────────────────

    /// <summary>A reopen of the connection (lost lock, reader refresh) empties the state and raises Changed.</summary>
    [Fact]
    public void Reset_EmptiesTheState_AndRaisesChanged()
    {
        var a = Seed(Make("A"));
        _history.Record("Edit HIST 101 A", () => { a.Notes = "x"; _sections.Update(a); });
        Assert.True(_history.CanUndo);
        var changed = _changed;

        _ctx.ReinitializeConnection(_path, readOnly: false);

        Assert.True(_changed > changed);
        Assert.False(_history.CanUndo);
        Assert.Empty(_history.UndoableSteps);
        Assert.True(_history.HasUnsavedChanges);                 // the changes are still unsaved, as one lump
        Assert.False(_history.HistoryReachesLastSave);

        _ctx.ReinitializeConnection(_path, readOnly: true);
        Assert.False(_history.IsAvailable);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>A section in Fall 2026 (or <paramref name="semesterId"/>) of HIST 101, with one Monday meeting.</summary>
    private static Section Make(string code, string semesterId = "sem1") => new()
    {
        SemesterId  = semesterId,
        CourseId    = "c1",
        SectionCode = code,
        Schedule    = { new SectionDaySchedule { Day = 1, StartMinutes = 600, DurationMinutes = 80 } },
    };

    /// <summary>Runs setup writes, then simulates a save: the history starts clean and the dirty signal is armed.</summary>
    /// <param name="setup">The writes.</param>
    private void Seed(Action setup)
    {
        setup();
        _signals.Save(_history);
        _ctx.ResetDirty();
        FirstWrites = 0;
    }

    /// <summary>Inserts a section as setup (see <see cref="Seed(Action)"/>).</summary>
    /// <param name="section">The section.</param>
    /// <returns>The same section.</returns>
    private Section Seed(Section section)
    {
        Seed(() => _sections.Insert(section));
        return section;
    }

    /// <summary>Deletes a scheduling-environment value the way the delete does: the value and its cleanup, recording paused.</summary>
    private void DeleteValue(string id, string type)
    {
        using var pause = _history.PauseRecording();
        foreach (var s in _sections.GetAll())
            if (SectionReferences.RemoveEnvironmentValue(s, type, id)) _sections.Update(s);
        new SchedulingEnvironmentRepository(_ctx).Delete(id);
    }

    /// <summary>The current row of a section.</summary>
    private SectionRow Row(string id) => _ctx.ChangeLog!.ReadCurrent(id)!;

    /// <summary>Every Sections row by id.</summary>
    private Dictionary<string, SectionRow> All()
    {
        var ids = new List<string>();
        using (var cmd = ((SqliteConnection)_ctx.Connection).CreateCommand())
        {
            cmd.CommandText = "SELECT id FROM main.Sections";
            using var r = cmd.ExecuteReader();
            while (r.Read()) ids.Add(r.GetString(0));
        }
        return ids.ToDictionary(id => id, Row);
    }

    /// <summary>Runs SQL directly on the context's connection.</summary>
    private void Raw(string sql)
    {
        using var cmd = ((SqliteConnection)_ctx.Connection).CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>The checkout service's save signals, raised by hand.</summary>
    internal sealed class FakeSignals : ISaveSignals
    {
        /// <inheritdoc/>
        public event Action? BecameDirty;
        /// <inheritdoc/>
        public event Action? SaveCompleted;
        /// <inheritdoc/>
        public event Action<long, bool>? SaveSucceeded;
        /// <inheritdoc/>
        public long EditGeneration { get; set; }
        /// <inheritdoc/>
        public bool HasDirtyMarker { get; set; }

        /// <summary>Raises <see cref="BecameDirty"/>.</summary>
        public void RaiseBecameDirty() => BecameDirty?.Invoke();

        /// <summary>Raises <see cref="SaveCompleted"/>.</summary>
        public void RaiseSaveCompleted() => SaveCompleted?.Invoke();

        /// <summary>Raises <see cref="SaveSucceeded"/>.</summary>
        public void RaiseSaveSucceeded(long editCount, bool keptMarker) => SaveSucceeded?.Invoke(editCount, keptMarker);

        /// <summary>A save of everything so far: completed, then succeeded at the current edit count, no marker kept.</summary>
        /// <param name="_">The history (only to make the call site read as what it does).</param>
        public void Save(SectionHistory _)
        {
            RaiseSaveCompleted();
            RaiseSaveSucceeded(EditGeneration, keptMarker: false);
        }
    }

    /// <summary>A card state that can be opened by hand.</summary>
    private sealed class FakeCard : ISectionCardState
    {
        /// <inheritdoc/>
        public string? OpenCardHeading { get; set; }
        /// <inheritdoc/>
        public event Action? OpenCardChanged { add { } remove { } }
    }

    /// <summary>A logger that keeps what it is told.</summary>
    private sealed class RecordingLogger : IAppLogger
    {
        /// <summary>The warnings logged.</summary>
        public List<string> Warnings { get; } = new();
        /// <summary>The errors logged.</summary>
        public List<Exception?> Errors { get; } = new();
        /// <inheritdoc/>
        public bool ThrowOnError { get; set; }
        /// <inheritdoc/>
        public event EventHandler<string>? ErrorLogged { add { } remove { } }
        /// <inheritdoc/>
        public void LogError(Exception? ex, string? context = null, bool unhandled = false) => Errors.Add(ex);
        /// <inheritdoc/>
        public void LogWarning(string message, string? context = null) => Warnings.Add(message);
        /// <inheritdoc/>
        public void LogInfo(string message, string? context = null) { }
        /// <inheritdoc/>
        public void LogBreadcrumb(string message, Dictionary<string, string>? metadata = null) { }
    }
}
