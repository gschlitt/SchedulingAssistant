using System.Reflection;
using Microsoft.Data.Sqlite;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests;

/// <summary>
/// Tests of the 1.2.5 undo change log (<see cref="ISectionChangeLog"/>, <see cref="SqliteSectionChangeLog"/>) against the
/// REAL <see cref="DatabaseContext"/> and the REAL save snapshot (<c>CheckoutService.SnapshotWorkingCopy</c>, by
/// reflection). They keep every guarantee of the step 1 probe (the log never reaches a copied file, emptying it is not
/// an edit to a save, entries roll back with their write, the log is gone with its connection, exact rows) and pin the
/// log's behaviour: steps, the barrier, pauses, "other changes", writing rows back, trimming after a save.
/// See <c>designs/1.2.5/undo.md</c> and <c>release_state/1.2.5/undo/NOTES.md</c>.
/// </summary>
public sealed class SectionChangeLogTests : IDisposable
{
    private readonly string _dir;
    private readonly ITestOutputHelper _out;

    /// <summary>Creates an isolated temporary folder.</summary>
    /// <param name="output">xUnit's output, for findings.</param>
    public SectionChangeLogTests(ITestOutputHelper output)
    {
        _out = output;
        _dir = Path.Combine(Path.GetTempPath(), $"sectionlog_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    /// <summary>Clears the pool and deletes the folder.</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // ── The log never reaches a copied file ──────────────────────────────────

    /// <summary>The log's tables and triggers leave the save's snapshot exactly as a database without a log.</summary>
    [Fact]
    public void Log_NeverReachesTheSnapshot()
    {
        var dPrime = Path.Combine(_dir, "wc.db");
        using var ctx = new DatabaseContext(dPrime);
        var repo = new SectionRepository(ctx);
        var a = NewSection("A"); repo.Insert(a);
        var b = NewSection("B"); repo.Insert(b);
        var log = ctx.ChangeLog!;
        Assert.True(log.IsAvailable);

        var f0 = SnapshotFingerprint(dPrime, "s0.db", out var s0);

        // The same edits on a twin whose log was stripped must give the same snapshot.
        var twin = Path.Combine(_dir, "twin.db");
        File.Copy(s0, twin);
        using var tctx = new DatabaseContext(twin);
        StripLog(tctx);
        var trepo = new SectionRepository(tctx);
        a.Schedule[0].StartMinutes = 900; trepo.Update(a);
        trepo.Delete(b.Id);
        var fTwin = SnapshotFingerprint(twin, "st.db", out _);

        var step = log.BeginStep("edits", DateTime.Now);
        repo.Update(a);
        repo.Delete(b.Id);
        log.EndStep(step, 2);
        Assert.Equal(2, log.GetEntries(step).Count);
        var f2 = SnapshotFingerprint(dPrime, "s2.db", out var s2);
        _out.WriteLine($"with log {f2[..12]}, twin without {fTwin[..12]}");
        Assert.Equal(fTwin, f2);

        using var snap = new SqliteConnection($"Data Source={s2};Mode=ReadOnly;Pooling=False");
        snap.Open();
        Assert.Equal(0, Scalar(snap, "SELECT COUNT(*) FROM sqlite_master WHERE name LIKE 'undo%' OR type = 'trigger'"));

        Exec(Conn(ctx), "PRAGMA wal_checkpoint(TRUNCATE)");
        using var raw = new SqliteConnection($"Data Source={dPrime};Mode=ReadOnly;Pooling=False");
        raw.Open();
        Assert.Equal(0, Scalar(raw, "SELECT COUNT(*) FROM sqlite_master WHERE name LIKE 'undo%' OR type = 'trigger'"));
    }

    /// <summary>Trimming the log while the save holds its snapshot connection does not change <c>data_version</c>; a real edit does.</summary>
    [Fact]
    public void TrimAfterSave_IsNotAnEditToTheSave()
    {
        var dPrime = Path.Combine(_dir, "wc.db");
        using var ctx = new DatabaseContext(dPrime);
        var repo = new SectionRepository(ctx);
        var a = NewSection("A"); repo.Insert(a);
        var log = ctx.ChangeLog!;
        Step(log, 1, () => { a.SectionCode = "A2"; repo.Update(a); });
        Step(log, 2, () => { a.SectionCode = "A3"; repo.Update(a); });

        using var source = Snapshot(dPrime, Path.Combine(_dir, "s.db"));
        var v0 = Scalar(source, "PRAGMA data_version");
        log.TrimAfterSave(1, resetFlags: true);
        Assert.Single(log.GetSteps());
        var v1 = Scalar(source, "PRAGMA data_version");
        a.SectionCode = "A4"; repo.Update(a);
        var v2 = Scalar(source, "PRAGMA data_version");
        _out.WriteLine($"data_version: before {v0}, after trimming {v1}, after a real edit {v2}");
        Assert.Equal(v0, v1);
        Assert.NotEqual(v1, v2);
    }

    /// <summary>An entry commits and rolls back with the repository write that made it; an empty step is deleted.</summary>
    [Fact]
    public void Entries_RollBackWithTheirWrite()
    {
        var dPrime = Path.Combine(_dir, "wc.db");
        using var ctx = new DatabaseContext(dPrime);
        var repo = new SectionRepository(ctx);
        var a = NewSection("A"); repo.Insert(a);
        var log = ctx.ChangeLog!;

        var step = log.BeginStep("rolled back", DateTime.Now);
        using (var tx = ctx.BeginWriteTransaction())
        {
            a.SectionCode = "X"; repo.Update(a, tx);
            repo.Insert(NewSection("B"), tx);
            Assert.Equal(2, Scalar(Conn(ctx), "SELECT COUNT(*) FROM temp.undo_entries", (SqliteTransaction)tx!));
            tx!.Rollback();
        }
        log.EndStep(step, 1);
        Assert.Empty(log.GetSteps());

        var step2 = log.BeginStep("committed", DateTime.Now);
        using (var tx = ctx.BeginWriteTransaction())
        {
            repo.Delete(a.Id, tx);
            tx!.Commit();
        }
        log.EndStep(step2, 2);
        Assert.Single(log.GetEntries(step2));
    }

    // ── The log's lifetime ───────────────────────────────────────────────────

    /// <summary>A reopen (lost lock, reader refresh) empties the history and raises Reset; read-only means no log.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Reinitialize_ResetsTheLog(bool readOnly)
    {
        var dPrime = Path.Combine(_dir, "wc.db");
        using var ctx = new DatabaseContext(dPrime);
        var repo = new SectionRepository(ctx);
        var log = ctx.ChangeLog!;
        Step(log, 1, () => repo.Insert(NewSection("A")));
        Assert.Single(log.GetSteps());
        var resets = 0;
        log.Reset += () => resets++;

        ctx.ReinitializeConnection(dPrime, readOnly);

        Assert.Equal(1, resets);
        Assert.Same(log, ctx.ChangeLog);
        Assert.Equal(!readOnly, log.IsAvailable);
        Assert.Empty(log.GetSteps());
        Assert.False(log.OtherChanges);
        Assert.False(log.HistoryIncomplete);
        Assert.Null(log.ReadCurrent("nothing"));
        Assert.Empty(log.GetEntries(1));
        if (readOnly)
            Assert.Throws<InvalidOperationException>(() => log.BeginStep("x", DateTime.Now));
        else
        {
            Assert.Null(log.InstallError);
            Assert.Equal(0, Scalar(Conn(ctx), "SELECT COUNT(*) FROM temp.undo_steps"));
            Step(log, 1, () => repo.Insert(NewSection("B")));   // usable again
            Assert.Single(log.GetSteps());
        }
    }

    /// <summary>A failed reopen still raises Reset and leaves the log unavailable.</summary>
    [Fact]
    public void Reinitialize_Failure_StillRaisesReset()
    {
        var dPrime = Path.Combine(_dir, "wc.db");
        using var ctx = new DatabaseContext(dPrime);
        var log = ctx.ChangeLog!;
        var resets = 0;
        log.Reset += () => resets++;

        Assert.Throws<InvalidOperationException>(() => ctx.ReinitializeConnection(Path.Combine(_dir, "missing.db"), readOnly: false));
        Assert.Equal(1, resets);
        Assert.False(log.IsAvailable);
    }

    /// <summary>A new context on the same path (a database switch, with or without CloseConnection) starts with an empty log.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewContextOnSamePath_StartsEmpty(bool closeFirst)
    {
        var dPrime = Path.Combine(_dir, "wc.db");
        var ctx = new DatabaseContext(dPrime);
        var log = ctx.ChangeLog!;
        Step(log, 1, () => new SectionRepository(ctx).Insert(NewSection("A")));
        if (closeFirst) ctx.CloseConnection();
        Assert.Equal(!closeFirst, log.IsAvailable);
        ctx.Dispose();
        Assert.False(log.IsAvailable);

        using var again = new DatabaseContext(dPrime);
        Assert.True(again.ChangeLog!.IsAvailable);
        Assert.Empty(again.ChangeLog.GetSteps());
        Assert.False(again.ChangeLog.OtherChanges);
        Assert.False(again.ChangeLog.HistoryIncomplete);
    }

    /// <summary>A read-only context has an unavailable log that never throws on reads.</summary>
    [Fact]
    public void ReadOnlyContext_HasNoLog()
    {
        var dPrime = Path.Combine(_dir, "wc.db");
        using (new DatabaseContext(dPrime)) { }
        using var ro = new DatabaseContext(dPrime, readOnly: true);
        var log = ro.ChangeLog!;
        Assert.False(log.IsAvailable);
        Assert.Null(log.InstallError);
        Assert.Empty(log.GetSteps());
        Assert.False(log.OtherChanges);
        Assert.False(log.HistoryIncomplete);
        Assert.Throws<InvalidOperationException>(() => log.BeginPause(ChangeLogPause.NotAStep));
        Assert.Throws<InvalidOperationException>(() => log.MarkHistoryIncomplete());
        log.EndStep(1, 1);   // no-ops
        log.EndPause();
    }

    /// <summary>
    /// Side fact (catch 1): WITHOUT <c>ClearAllPools</c> a pooled connection hands its TEMP tables to the next opener of
    /// the same connection string. Documents why every close path of <see cref="DatabaseContext"/> keeps clearing the pool.
    /// </summary>
    [Fact]
    public void SideFact_PoolCarriesTempTables()
    {
        var path = Path.Combine(_dir, "pool.db");
        var cs = $"Data Source={path}";
        using (var c1 = new SqliteConnection(cs))
        {
            c1.Open();
            Exec(c1, "CREATE TEMP TABLE t(x); INSERT INTO t VALUES (1);");
        }
        long carried;
        using (var c2 = new SqliteConnection(cs))
        {
            c2.Open();
            carried = Scalar(c2, "SELECT COUNT(*) FROM sqlite_temp_master WHERE name = 't'");
        }
        _out.WriteLine($"TEMP table carried to the next pooled opener without ClearAllPools: {carried == 1}");
        SqliteConnection.ClearAllPools();
        using var c3 = new SqliteConnection(cs);
        c3.Open();
        Assert.Equal(0, Scalar(c3, "SELECT COUNT(*) FROM sqlite_temp_master WHERE name = 't'"));
    }

    // ── Steps ────────────────────────────────────────────────────────────────

    /// <summary>One step around an insert, an update and a delete records entries with the exact old and new rows.</summary>
    [Fact]
    public void Step_RecordsExactRows()
    {
        using var ctx = new DatabaseContext(Path.Combine(_dir, "wc.db"));
        var repo = new SectionRepository(ctx);
        var log = ctx.ChangeLog!;
        var a = NewSection("A"); repo.Insert(a);
        var b = NewSection("B"); repo.Insert(b);
        var aBefore = log.ReadCurrent(a.Id)!;
        var bBefore = log.ReadCurrent(b.Id)!;
        log.TrimAfterSave(long.MaxValue, resetFlags: true);   // the setup writes were barriers
        var at = new DateTime(2026, 11, 26, 10, 58, 0, DateTimeKind.Local);

        var step = log.BeginStep("Do things", at);
        var c = NewSection("C"); repo.Insert(c);
        a.Schedule[0].Day = 4; repo.Update(a);
        repo.Delete(b.Id);
        log.EndStep(step, 3);

        var aAfter = log.ReadCurrent(a.Id)!;
        var cAfter = log.ReadCurrent(c.Id)!;
        Assert.NotEqual(aBefore, aAfter);
        Assert.Null(log.ReadCurrent(b.Id));

        var info = Assert.Single(log.GetSteps());
        Assert.Equal(("Do things", at, 3L, false), (info.Label, info.At, info.EditCount, info.IsUndone));
        Assert.Equal(step, info.Id);
        var e = log.GetEntries(step);
        Assert.Equal(3, e.Count);
        Assert.True(e[0].Seq < e[1].Seq && e[1].Seq < e[2].Seq);
        Assert.Equal((SectionLogOp.Insert, c.Id, (SectionRow?)null, cAfter), (e[0].Op, e[0].SectionId, e[0].Old, e[0].New));
        Assert.Equal((SectionLogOp.Update, a.Id, aBefore, aAfter), (e[1].Op, e[1].SectionId, e[1].Old, e[1].New));
        Assert.Equal((SectionLogOp.Delete, b.Id, bBefore, (SectionRow?)null), (e[2].Op, e[2].SectionId, e[2].Old, e[2].New));
        Assert.False(log.HistoryIncomplete);
        Assert.False(log.OtherChanges);
    }

    /// <summary>An update that changes nothing leaves no entry, and its step is deleted.</summary>
    [Fact]
    public void Step_WithNoRealChange_IsDeleted()
    {
        using var ctx = new DatabaseContext(Path.Combine(_dir, "wc.db"));
        var repo = new SectionRepository(ctx);
        var log = ctx.ChangeLog!;
        var a = NewSection("A"); repo.Insert(a);
        log.TrimAfterSave(long.MaxValue, resetFlags: true);   // the setup write was a barrier

        var step = log.BeginStep("no-op", DateTime.Now);
        repo.Update(a);
        Assert.Single(log.GetEntries(step));       // recorded while the step runs
        log.EndStep(step, 1);

        Assert.Empty(log.GetEntries(step));
        Assert.Empty(log.GetSteps());
        Assert.False(log.HistoryIncomplete);

        // A real change next to a no-op update keeps only the real one.
        var b = NewSection("B"); repo.Insert(b);
        var step2 = log.BeginStep("mixed", DateTime.Now);
        repo.Update(a);
        b.Notes = "changed"; repo.Update(b);
        log.EndStep(step2, 2);
        Assert.Equal(b.Id, Assert.Single(log.GetEntries(step2)).SectionId);
    }

    /// <summary>Steps and pauses do not nest; EndStep clears the step even when it recorded nothing.</summary>
    [Fact]
    public void Nesting_Throws()
    {
        using var ctx = new DatabaseContext(Path.Combine(_dir, "wc.db"));
        var log = ctx.ChangeLog!;
        var step = log.BeginStep("one", DateTime.Now);
        Assert.Throws<InvalidOperationException>(() => log.BeginStep("two", DateTime.Now));
        Assert.Throws<InvalidOperationException>(() => log.BeginPause(ChangeLogPause.NotAStep));
        Assert.Throws<InvalidOperationException>(() => log.WriteRows(step, Array.Empty<(string, SectionRow?)>(), true));
        log.EndStep(step, 0);

        log.BeginPause(ChangeLogPause.NotAStep);
        Assert.Throws<InvalidOperationException>(() => log.BeginStep("three", DateTime.Now));
        Assert.Throws<InvalidOperationException>(() => log.BeginPause(ChangeLogPause.WritingHistory));
        log.EndPause();

        Assert.Throws<ArgumentException>(() => log.BeginPause(ChangeLogPause.None));
        log.EndStep(log.BeginStep("four", DateTime.Now), 0);   // usable again
    }

    /// <summary>A new step deletes the redo list (undone steps and their entries) and keeps the done steps.</summary>
    [Fact]
    public void BeginStep_ClearsTheRedoList()
    {
        using var ctx = new DatabaseContext(Path.Combine(_dir, "wc.db"));
        var repo = new SectionRepository(ctx);
        var log = ctx.ChangeLog!;
        var a = NewSection("A"); repo.Insert(a);
        var s1 = Step(log, 1, () => { a.SectionCode = "A1"; repo.Update(a); });
        var s2 = Step(log, 2, () => { a.SectionCode = "A2"; repo.Update(a); });
        Undo(log, s2);
        Assert.True(Assert.Single(log.GetSteps(), s => s.Id == s2).IsUndone);

        var s3 = Step(log, 3, () => { a.SectionCode = "A3"; repo.Update(a); });

        Assert.Equal(new[] { s1, s3 }, log.GetSteps().Select(s => s.Id).ToArray());
        Assert.Empty(log.GetEntries(s2));
        Assert.Equal(0, Scalar(Conn(ctx), "SELECT COUNT(*) FROM temp.undo_entries WHERE step_id = " + s2));
    }

    // ── Barrier, pauses, other tables ────────────────────────────────────────

    /// <summary>A section write with neither a step nor a pause empties both lists and sets HistoryIncomplete.</summary>
    [Fact]
    public void Barrier_EmptiesBothListsAndFlags()
    {
        using var ctx = new DatabaseContext(Path.Combine(_dir, "wc.db"));
        var repo = new SectionRepository(ctx);
        var log = ctx.ChangeLog!;
        var a = NewSection("A"); repo.Insert(a);          // itself a barrier: nothing to cut yet
        log.TrimAfterSave(long.MaxValue, resetFlags: true);
        Assert.False(log.HistoryIncomplete);

        Step(log, 1, () => { a.SectionCode = "A1"; repo.Update(a); });
        var s2 = Step(log, 2, () => { a.SectionCode = "A2"; repo.Update(a); });
        Undo(log, s2);
        Assert.Equal(2, log.GetSteps().Count);

        repo.Insert(NewSection("B"));                     // a path the design missed

        Assert.Empty(log.GetSteps());
        Assert.Equal(0, Scalar(Conn(ctx), "SELECT COUNT(*) FROM temp.undo_entries"));
        Assert.True(log.HistoryIncomplete);
        Assert.False(log.OtherChanges);

        // Each event is a barrier.
        log.TrimAfterSave(long.MaxValue, resetFlags: true);
        Step(log, 3, () => { a.SectionCode = "A3"; repo.Update(a); });
        repo.Delete(a.Id);
        Assert.Empty(log.GetSteps());
        Assert.True(log.HistoryIncomplete);
        log.TrimAfterSave(long.MaxValue, resetFlags: true);
        Step(log, 4, () => repo.Insert(NewSection("C")));
        var c = NewSection("D"); repo.Insert(c);
        Assert.Empty(log.GetSteps());
        repo.Update(c);
        Assert.True(log.HistoryIncomplete);
    }

    /// <summary>NotAStep: nothing recorded, OtherChanges set, existing steps kept. WritingHistory: nothing recorded, nothing flagged.</summary>
    [Fact]
    public void Pauses_RecordNothing()
    {
        using var ctx = new DatabaseContext(Path.Combine(_dir, "wc.db"));
        var repo = new SectionRepository(ctx);
        var log = ctx.ChangeLog!;
        var a = NewSection("A"); repo.Insert(a);
        var s1 = Step(log, 1, () => { a.SectionCode = "A1"; repo.Update(a); });
        log.TrimAfterSave(0, resetFlags: true);
        Assert.False(log.OtherChanges);

        log.BeginPause(ChangeLogPause.WritingHistory);
        a.SectionCode = "quiet"; repo.Update(a);
        repo.Insert(NewSection("Q"));
        log.EndPause();
        Assert.False(log.OtherChanges);
        Assert.False(log.HistoryIncomplete);
        Assert.Single(log.GetSteps());
        Assert.Equal(1, log.GetEntries(s1).Count);

        log.BeginPause(ChangeLogPause.NotAStep);
        var b = NewSection("B"); repo.Insert(b);
        b.SectionCode = "B2"; repo.Update(b);
        repo.Delete(b.Id);
        log.EndPause();
        Assert.True(log.OtherChanges);
        Assert.False(log.HistoryIncomplete);
        Assert.Equal(s1, Assert.Single(log.GetSteps()).Id);
        Assert.Equal(1, log.GetEntries(s1).Count);
        Assert.Equal(1, Scalar(Conn(ctx), "SELECT COUNT(*) FROM temp.undo_entries"));
    }

    /// <summary>A write to any other table sets OtherChanges and keeps the history.</summary>
    [Fact]
    public void OtherTables_SetOtherChanges_AndKeepTheHistory()
    {
        using var ctx = new DatabaseContext(Path.Combine(_dir, "wc.db"));
        var repo = new SectionRepository(ctx);
        var log = ctx.ChangeLog!;
        var a = NewSection("A"); repo.Insert(a);
        var s1 = Step(log, 1, () => { a.SectionCode = "A1"; repo.Update(a); });
        log.TrimAfterSave(0, resetFlags: true);   // keep s1? it's at count 1, so it stays
        Assert.False(log.OtherChanges);

        var rooms = new RoomRepository(ctx);
        var room = new Room { Building = "B", RoomNumber = "204" };
        rooms.Insert(room);
        Assert.True(log.OtherChanges);

        log.TrimAfterSave(0, resetFlags: true);
        Assert.False(log.OtherChanges);
        room.Notes = "n"; rooms.Update(room);
        Assert.True(log.OtherChanges);

        log.TrimAfterSave(0, resetFlags: true);
        rooms.Delete(room.Id);
        Assert.True(log.OtherChanges);

        log.TrimAfterSave(0, resetFlags: true);
        Exec(Conn(ctx), "INSERT INTO SchedulingEnvironmentValues(id, type, name) VALUES ('v1', 'tag', 'Lab')");
        Assert.True(log.OtherChanges);

        // Also during a NotAStep or WritingHistory pause, and never touching the history.
        log.TrimAfterSave(0, resetFlags: true);
        log.BeginPause(ChangeLogPause.WritingHistory);
        Exec(Conn(ctx), "DELETE FROM SchedulingEnvironmentValues WHERE id = 'v1'");
        log.EndPause();
        Assert.True(log.OtherChanges);
        Assert.False(log.HistoryIncomplete);
        Assert.Equal(s1, Assert.Single(log.GetSteps()).Id);
        Assert.Single(log.GetEntries(s1));
    }

    // ── Writing rows back ────────────────────────────────────────────────────

    /// <summary>
    /// Undoing a step from its old rows (newest entry first) restores every column of every row exactly, including
    /// meetings, a JSON field from a newer version, and a deleted section re-inserted with its id; redoing from the new
    /// rows restores the edited state; the undone mark is stored.
    /// </summary>
    [Fact]
    public void WriteRows_UndoAndRedo_AreExact()
    {
        var dPrime = Path.Combine(_dir, "wc.db");
        using var ctx = new DatabaseContext(dPrime);
        var repo = new SectionRepository(ctx);
        var log = ctx.ChangeLog!;
        var sections = Enumerable.Range(0, 12).Select(i => NewSection($"S{i}")).ToList();
        sections[2].Schedule.Add(new SectionDaySchedule { Day = 3, StartMinutes = 780, DurationMinutes = 50, RoomId = "r1" });
        foreach (var s in sections) repo.Insert(s);
        // A field a newer TermPoint wrote, which this version does not know.
        Exec(Conn(ctx), $"UPDATE Sections SET data = json_set(data, '$.fromTheFuture', json('{{\"k\":[1,2]}}')) WHERE id = '{sections[3].Id}'");
        log.TrimAfterSave(long.MaxValue, resetFlags: true);
        var before = ReadAll(ctx);

        var created = NewSection("NEW");
        var step = Step(log, 1, () =>
        {
            sections[0].Schedule[0].StartMinutes = 840; repo.Update(sections[0]);
            sections[0].Schedule[0].Day = 3;            repo.Update(sections[0]);
            sections[1].Notes = "edited";               repo.Update(sections[1]);
            sections[2].Schedule.RemoveAt(1);           repo.Update(sections[2]);
            repo.Delete(sections[3].Id);
            repo.Insert(created);
            var copy = NewSection("COPY"); repo.Insert(copy); repo.Delete(copy.Id);
            for (var i = 6; i < 10; i++) repo.Delete(sections[i].Id);
        });
        var after = ReadAll(ctx);
        Assert.NotEqual(before[sections[0].Id], after[sections[0].Id]);
        var entries = log.GetEntries(step);
        Assert.Equal(12, entries.Count);

        Undo(log, step);
        Assert.Equal(before, ReadAll(ctx));
        Assert.Contains("fromTheFuture", ReadAll(ctx)[sections[3].Id].Data);
        Assert.True(log.GetSteps().Single().IsUndone);
        Assert.False(log.HistoryIncomplete);
        Assert.False(log.OtherChanges);
        Assert.Equal(12, log.GetEntries(step).Count);       // writing history records nothing

        Redo(log, step);
        Assert.Equal(after, ReadAll(ctx));
        Assert.False(log.GetSteps().Single().IsUndone);
    }

    /// <summary>A failure inside WriteRows rolls back every write and the undone mark, and clears the pause.</summary>
    [Fact]
    public void WriteRows_Failure_RollsBackEverything()
    {
        using var ctx = new DatabaseContext(Path.Combine(_dir, "wc.db"));
        var repo = new SectionRepository(ctx);
        var log = ctx.ChangeLog!;
        var a = NewSection("A"); repo.Insert(a);
        log.TrimAfterSave(long.MaxValue, resetFlags: true);   // the setup write was a barrier
        var step = Step(log, 1, () => { a.SectionCode = "A1"; repo.Update(a); });
        var before = ReadAll(ctx);
        var good = log.GetEntries(step).Single().Old!;
        var bad = new SectionRow("x", "sem1", null, null, null, null, null, null, null!);   // data is NOT NULL

        Assert.ThrowsAny<Exception>(() => log.WriteRows(step,
            new (string, SectionRow?)[] { (a.Id, good), ("x", bad) }, markUndone: true));

        Assert.Equal(before, ReadAll(ctx));
        Assert.False(log.GetSteps().Single().IsUndone);
        Assert.False(log.HistoryIncomplete);
        log.EndStep(log.BeginStep("pause was cleared", DateTime.Now), 0);   // BeginStep would throw if a pause were left
    }

    /// <summary>
    /// The readable columns of a row written back are recomputed from the course, semester and academic year as they are
    /// now (a course whose calendar code changed after the step does not bring its old code back); every other column,
    /// including the JSON, is exactly as logged.
    /// </summary>
    [Fact]
    public void WriteRows_RecomputesTheReadableColumns()
    {
        using var ctx = new DatabaseContext(Path.Combine(_dir, "wc.db"));
        var repo = new SectionRepository(ctx);
        var log = ctx.ChangeLog!;
        Exec(Conn(ctx), "INSERT INTO AcademicYears(id, name, data) VALUES ('ay1', '2026-2027', '{}')");
        Exec(Conn(ctx), "INSERT INTO Semesters(id, academic_year_id, academic_year_name, name, data) VALUES ('sem1', 'ay1', '2026-2027', 'Fall 2026', '{}')");
        Exec(Conn(ctx), "INSERT INTO Courses(id, subject_id, calendar_code, title, data) VALUES ('c1', 's', 'OLD 101', 't', '{\"calendarCode\":\"OLD 101\"}')");
        var a = NewSection("A"); repo.Insert(a);
        log.TrimAfterSave(long.MaxValue, resetFlags: true);
        var step = Step(log, 1, () => { a.Notes = "edited"; repo.Update(a); });
        var old = log.GetEntries(step).Single().Old!;
        Assert.Equal(("OLD 101", "Fall 2026", "2026-2027"), (old.CourseCode, old.SemesterName, old.AcademicYearName));

        Exec(Conn(ctx), "UPDATE Courses SET data = json_set(data, '$.calendarCode', 'NEW 101') WHERE id = 'c1'");
        Exec(Conn(ctx), "UPDATE Semesters SET name = 'Autumn 2026' WHERE id = 'sem1'");
        Undo(log, step);

        var now = log.ReadCurrent(a.Id)!;
        Assert.Equal(("NEW 101", "Autumn 2026", "2026-2027"), (now.CourseCode, now.SemesterName, now.AcademicYearName));
        Assert.Equal(old with { CourseCode = "NEW 101", SemesterName = "Autumn 2026" }, now);   // every other column as logged

        // A section re-inserted from a delete gets the current readable columns too.
        var del = Step(log, 2, () => repo.Delete(a.Id));
        Exec(Conn(ctx), "UPDATE Courses SET data = json_set(data, '$.calendarCode', 'NEWER 101') WHERE id = 'c1'");
        Undo(log, del);
        Assert.Equal("NEWER 101", log.ReadCurrent(a.Id)!.CourseCode);
    }

    // ── Deleted this session ─────────────────────────────────────────────────

    /// <summary>
    /// A delete in any table with an id and a data column is kept (WasDeleted, DeletedData); a row put back under the same
    /// id, a row never deleted and a table without those columns are not "deleted"; the other-changes guard still holds
    /// (a flag already set writes nothing more); a reopen forgets everything.
    /// </summary>
    [Fact]
    public void Deleted_IsTrackedForTablesWithIdAndData()
    {
        var dPrime = Path.Combine(_dir, "wc.db");
        using var ctx = new DatabaseContext(dPrime);
        var log = ctx.ChangeLog!;
        var rooms = new RoomRepository(ctx);
        var room = new Room { Building = "B", RoomNumber = "204" };
        rooms.Insert(room);
        var other = new Room { Building = "C", RoomNumber = "1" };
        rooms.Insert(other);
        Exec(Conn(ctx), "INSERT INTO SchedulingEnvironmentValues(id, type, name, data) VALUES ('t1', 'tag', 'Lab', '{\"name\":\"Lab\"}')");
        Exec(Conn(ctx), "INSERT INTO LegalStartTimes(academic_year_id, block_length, start_times) VALUES ('ay', 1.5, '[]')");
        Assert.False(log.WasDeleted("Rooms", room.Id));

        rooms.Delete(room.Id);
        Exec(Conn(ctx), "DELETE FROM SchedulingEnvironmentValues WHERE id = 't1'");
        Exec(Conn(ctx), "DELETE FROM LegalStartTimes");

        Assert.True(log.WasDeleted("Rooms", room.Id));
        Assert.Contains("204", log.DeletedData("Rooms", room.Id));
        Assert.True(log.WasDeleted("SchedulingEnvironmentValues", "t1"));
        Assert.Contains("Lab", log.DeletedData("SchedulingEnvironmentValues", "t1"));
        Assert.False(log.WasDeleted("Rooms", other.Id));
        Assert.Null(log.DeletedData("Rooms", other.Id));
        Assert.False(log.WasDeleted("LegalStartTimes", "ay"));
        Assert.False(log.WasDeleted("NoSuchTable", room.Id));
        Assert.True(log.OtherChanges);

        // Put back under the same id: not deleted any more.
        rooms.Insert(room);
        Assert.False(log.WasDeleted("Rooms", room.Id));
        Assert.Null(log.DeletedData("Rooms", room.Id));

        // Never trimmed by a save; gone with the connection.
        rooms.Delete(room.Id);
        log.TrimAfterSave(long.MaxValue, resetFlags: true);
        Assert.True(log.WasDeleted("Rooms", room.Id));
        ctx.ReinitializeConnection(dPrime, readOnly: false);
        Assert.False(log.WasDeleted("Rooms", room.Id));
    }

    /// <summary>ClearHistory empties both lists and sets HistoryIncomplete, and is refused without a log.</summary>
    [Fact]
    public void ClearHistory_EmptiesAndFlags()
    {
        using var ctx = new DatabaseContext(Path.Combine(_dir, "wc.db"));
        var repo = new SectionRepository(ctx);
        var log = ctx.ChangeLog!;
        var a = NewSection("A"); repo.Insert(a);
        log.TrimAfterSave(long.MaxValue, resetFlags: true);
        Step(log, 1, () => { a.SectionCode = "A1"; repo.Update(a); });
        var s2 = Step(log, 2, () => { a.SectionCode = "A2"; repo.Update(a); });
        Undo(log, s2);

        log.ClearHistory();

        Assert.Empty(log.GetSteps());
        Assert.Equal(0, Scalar(Conn(ctx), "SELECT COUNT(*) FROM temp.undo_entries"));
        Assert.True(log.HistoryIncomplete);
        ctx.CloseConnection();
        Assert.Throws<InvalidOperationException>(() => log.ClearHistory());
    }

    // ── Trimming ─────────────────────────────────────────────────────────────

    /// <summary>
    /// TrimAfterSave drops steps at or below the edit count and every undone step, keeps later steps, and resets the
    /// flags only when asked.
    /// </summary>
    [Fact]
    public void TrimAfterSave_DropsSavedAndUndoneSteps()
    {
        using var ctx = new DatabaseContext(Path.Combine(_dir, "wc.db"));
        var repo = new SectionRepository(ctx);
        var log = ctx.ChangeLog!;
        var a = NewSection("A"); repo.Insert(a);
        var steps = Enumerable.Range(1, 5).Select(i => Step(log, i, () => { a.SectionCode = $"A{i}"; repo.Update(a); })).ToList();
        Undo(log, steps[4]);                         // redo list: step 5 (edit count 5)
        log.MarkHistoryIncomplete();
        log.BeginPause(ChangeLogPause.NotAStep); repo.Insert(NewSection("Z")); log.EndPause();
        Assert.True(log.OtherChanges);

        log.TrimAfterSave(2, resetFlags: false);
        Assert.Equal(new[] { steps[2], steps[3] }, log.GetSteps().Select(s => s.Id).ToArray());
        Assert.True(log.OtherChanges);
        Assert.True(log.HistoryIncomplete);
        Assert.Empty(log.GetEntries(steps[0]));
        Assert.Empty(log.GetEntries(steps[4]));
        Assert.Single(log.GetEntries(steps[2]));
        Assert.Equal(2, Scalar(Conn(ctx), "SELECT COUNT(*) FROM temp.undo_entries"));

        log.TrimAfterSave(4, resetFlags: true);
        Assert.Empty(log.GetSteps());
        Assert.False(log.OtherChanges);
        Assert.False(log.HistoryIncomplete);

        // A step still being made is never trimmed.
        var open = log.BeginStep("open", DateTime.Now);
        a.SectionCode = "open"; repo.Update(a);
        log.TrimAfterSave(long.MaxValue, resetFlags: false);
        Assert.Single(log.GetEntries(open));
        log.EndStep(open, 6);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>A section with one meeting.</summary>
    private static Section NewSection(string code) => new()
    {
        SemesterId  = "sem1",
        CourseId    = "c1",
        SectionCode = code,
        Schedule    = { new SectionDaySchedule { Day = 1, StartMinutes = 600, DurationMinutes = 80 } },
    };

    /// <summary>Runs <paramref name="action"/> as one step, the way a caller would (BeginStep, work, EndStep in a finally).</summary>
    /// <param name="log">The log.</param>
    /// <param name="editCount">The edit count to store on the step.</param>
    /// <param name="action">The repository writes.</param>
    /// <returns>The step's id (the step is deleted when the action changed nothing).</returns>
    private static long Step(ISectionChangeLog log, long editCount, Action action)
    {
        var id = log.BeginStep($"step {editCount}", DateTime.Now);
        try { action(); }
        finally { log.EndStep(id, editCount); }
        return id;
    }

    /// <summary>Undoes a step the way the later history service will: old rows, newest entry first.</summary>
    private static void Undo(ISectionChangeLog log, long stepId)
        => log.WriteRows(stepId,
            log.GetEntries(stepId).Reverse().Select(e => (e.SectionId, e.Old)).ToList(), markUndone: true);

    /// <summary>Redoes a step: new rows in recording order.</summary>
    private static void Redo(ISectionChangeLog log, long stepId)
        => log.WriteRows(stepId, log.GetEntries(stepId).Select(e => (e.SectionId, e.New)).ToList(), markUndone: false);

    /// <summary>Drops the log's TEMP objects so the context behaves like a database that never had a log.</summary>
    private static void StripLog(DatabaseContext ctx)
    {
        var names = new List<(string Type, string Name)>();
        using (var cmd = Conn(ctx).CreateCommand())
        {
            cmd.CommandText = "SELECT type, name FROM sqlite_temp_master WHERE name LIKE 'undo%' ORDER BY type = 'table'";
            using var r = cmd.ExecuteReader();
            while (r.Read()) names.Add((r.GetString(0), r.GetString(1)));
        }
        foreach (var (type, name) in names) Exec(Conn(ctx), $"DROP {type} temp.\"{name}\"");
        Assert.Equal(0, Scalar(Conn(ctx), "SELECT COUNT(*) FROM sqlite_temp_master WHERE name LIKE 'undo%'"));
    }

    /// <summary>The context's connection.</summary>
    private static SqliteConnection Conn(DatabaseContext ctx) => (SqliteConnection)ctx.Connection;

    /// <summary>Every Sections row by id, all columns.</summary>
    private static Dictionary<string, SectionRow> ReadAll(DatabaseContext ctx)
    {
        var rows = new Dictionary<string, SectionRow>();
        using var cmd = Conn(ctx).CreateCommand();
        cmd.CommandText = "SELECT id FROM main.Sections";
        var ids = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read()) ids.Add(r.GetString(0));
        foreach (var id in ids) rows[id] = ctx.ChangeLog!.ReadCurrent(id)!;
        return rows;
    }

    /// <summary>Takes the save's real snapshot of <paramref name="source"/>; returns the open source connection it hands back.</summary>
    private static SqliteConnection Snapshot(string source, string dest)
    {
        var mi = typeof(CheckoutService).GetMethod("SnapshotWorkingCopy", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (SqliteConnection)mi.Invoke(null, new object?[] { source, dest, null })!;
    }

    /// <summary>Snapshots <paramref name="source"/> to a file in the folder and returns its content fingerprint.</summary>
    private string SnapshotFingerprint(string source, string name, out string destPath)
    {
        destPath = Path.Combine(_dir, name);
        Snapshot(source, destPath).Dispose();
        return SqliteContentHash.Compute(destPath);
    }

    /// <summary>Runs a scalar query, optionally in a transaction.</summary>
    private static long Scalar(SqliteConnection c, string sql, SqliteTransaction? tx = null)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>Runs SQL.</summary>
    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
