using Microsoft.Data.Sqlite;
using SchedulingAssistant.Data;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SchedulingAssistant.Tests;

/// <summary>
/// Diagnostic tests for the reader-instance Refresh facility.
///
/// <para>
/// These tests isolate the data-access layer to verify that a <see cref="SectionStore"/>
/// reload correctly picks up commits made through a separate <see cref="DatabaseContext"/>
/// (simulating a writer process), and to pinpoint the root cause of the bug where
/// clicking "Refresh" in the read-only banner showed stale data.
/// </para>
///
/// <para>
/// Each test opens two <see cref="DatabaseContext"/> instances against the same SQLite
/// file — a <em>writer</em> and a <em>reader</em> — to replicate the two-process scenario.
/// No Avalonia UI thread or ViewModels are involved; the layer under test is
/// <see cref="SectionStore"/> + <see cref="SectionRepository"/>.
/// </para>
///
/// <para>Tests are organised into two groups:
/// <list type="bullet">
///   <item><description>
///     Fresh reads — <see cref="SectionStore.Reload"/> always returns up-to-date data.
///   </description></item>
///   <item><description>
///     Bug diagnosis — reading the in-memory cache without calling
///     <see cref="SectionStore.Reload"/> produces stale data, which is exactly what
///     <c>SectionListViewModel.Reload()</c> (the pre-fix <c>RefreshData</c> path) did.
///   </description></item>
/// </list>
/// </para>
/// </summary>
public sealed class RefreshTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly string _tempDir;
    private readonly string _dbPath;

    /// <summary>
    /// Creates a unique temporary directory for this test instance.
    /// A real SQLite database file is placed inside it.
    /// </summary>
    public RefreshTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"refresh_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test.db");
    }

    /// <summary>Deletes the temporary directory and all files created during the test.</summary>
    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Inserts a minimal Semester row directly via SQL so that sections can be
    /// inserted under it without requiring the full SemesterRepository setup.
    /// </summary>
    /// <param name="conn">An open <see cref="SqliteConnection"/> with the schema already created.</param>
    /// <param name="semesterId">The GUID string to use as the semester primary key.</param>
    private static void InsertSemester(SqliteConnection conn, string semesterId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT OR IGNORE INTO Semesters (id, academic_year_id, name, sort_order) VALUES ($id, '', 'Test Semester', 0)";
        cmd.Parameters.AddWithValue("$id", semesterId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Builds a minimal <see cref="Section"/> for insertion into the test database.
    /// </summary>
    /// <param name="semesterId">The semester to place the section in.</param>
    /// <param name="sectionCode">A human-readable code used to distinguish sections in assertions.</param>
    /// <param name="instructorId">
    /// Optional instructor ID to add as an <see cref="InstructorAssignment"/>.
    /// Omit to create a section with no instructor.
    /// </param>
    /// <returns>A new <see cref="Section"/> with a random ID.</returns>
    private static Section MakeSection(string semesterId, string sectionCode, string? instructorId = null)
    {
        var section = new Section
        {
            Id = Guid.NewGuid().ToString(),
            SemesterId = semesterId,
            SectionCode = sectionCode,
        };

        if (instructorId is not null)
            section.InstructorAssignments.Add(new InstructorAssignment { InstructorId = instructorId });

        return section;
    }

    // ── Group 1: SectionStore.Reload always reads fresh data from disk ─────────

    /// <summary>
    /// A reader-side <see cref="SectionStore"/> that calls <see cref="SectionStore.Reload"/>
    /// against the same SQLite file as the writer will see the initial data correctly.
    /// This is the basic sanity check before testing cross-connection visibility.
    /// </summary>
    [Fact]
    public void SectionStore_InitialReload_SeesDataInsertedByWriterConnection()
    {
        var semId = Guid.NewGuid().ToString();

        // Writer opens the DB, creates the schema, and inserts a section.
        using var writerDb = new DatabaseContext(_dbPath);
        InsertSemester(writerDb.Connection, semId);
        var writerRepo = new SectionRepository(writerDb);
        var section = MakeSection(semId, "A1", "instructor-alpha");
        writerRepo.Insert(section);

        // Reader opens the same file with a completely separate SqliteConnection.
        using var readerDb = new DatabaseContext(_dbPath);
        var readerRepo = new SectionRepository(readerDb);
        var store = new SectionStore();

        store.Reload(readerRepo, [semId]);

        Assert.Single(store.Sections);
        Assert.Equal("A1", store.Sections[0].SectionCode);
        Assert.Equal("instructor-alpha", store.Sections[0].InstructorAssignments.Single().InstructorId);
    }

    /// <summary>
    /// After the writer commits a change (updating an instructor assignment) and the
    /// reader calls <see cref="SectionStore.Reload"/>, the reader's cache reflects
    /// the new data.  This confirms that the underlying SQLite read returns fresh rows
    /// on every query, and that <see cref="SectionStore.Reload"/> is the correct hook
    /// for the Refresh button to use.
    /// </summary>
    [Fact]
    public void SectionStore_Reload_SeesInstructorChangeCommittedByWriterConnection()
    {
        var semId = Guid.NewGuid().ToString();

        // ── Writer: insert initial section with "instructor-before" ──
        using var writerDb = new DatabaseContext(_dbPath);
        InsertSemester(writerDb.Connection, semId);
        var writerRepo = new SectionRepository(writerDb);
        var section = MakeSection(semId, "A1", "instructor-before");
        writerRepo.Insert(section);

        // ── Reader: prime the cache ──
        using var readerDb = new DatabaseContext(_dbPath);
        var readerRepo = new SectionRepository(readerDb);
        var store = new SectionStore();

        store.Reload(readerRepo, [semId]);
        Assert.Equal("instructor-before", store.Sections.Single().InstructorAssignments.Single().InstructorId);

        // ── Writer: update the instructor (simulates the user editing on the writer instance) ──
        section.InstructorAssignments.Clear();
        section.InstructorAssignments.Add(new InstructorAssignment { InstructorId = "instructor-after" });
        writerRepo.Update(section);

        // ── Reader: call Reload again — should pick up the writer's committed change ──
        store.Reload(readerRepo, [semId]);

        Assert.True(
            store.Sections.Single().InstructorAssignments.Single().InstructorId == "instructor-after",
            "SectionStore.Reload() must return the writer's committed data.");
    }

    // ── Group 2: Bug diagnosis — stale cache without Reload ────────────────────

    /// <summary>
    /// Demonstrates that the in-memory <see cref="SectionStore"/> cache is stale after
    /// a writer commits a change, as long as the reader does <em>not</em> call
    /// <see cref="SectionStore.Reload"/>.
    ///
    /// <para>
    /// This is the root cause of the Refresh button bug.
    /// <c>SectionListViewModel.Reload()</c> calls <c>Load()</c>, which rebuilds the
    /// displayed list from whatever is already in <see cref="SectionStore.Sections"/>.
    /// It does <em>not</em> call <c>_sectionStore.Reload(repo, semIds)</c>, so the
    /// cache is never refreshed from the database.  As a result, the reader sees
    /// the pre-change data even after clicking Refresh.
    /// </para>
    ///
    /// <para>
    /// The fix is to have <c>MainWindowViewModel.RefreshData()</c> invoke a method
    /// that calls <c>_sectionStore.Reload(repo, semIds)</c> before rebuilding the
    /// view, rather than the public <c>SectionListVm.Reload()</c> shortcut.
    /// </para>
    /// </summary>
    [Fact]
    public void SectionStore_WithoutReload_CacheIsStaleAfterWriterCommits()
    {
        var semId = Guid.NewGuid().ToString();

        using var writerDb = new DatabaseContext(_dbPath);
        InsertSemester(writerDb.Connection, semId);
        var writerRepo = new SectionRepository(writerDb);
        var section = MakeSection(semId, "A1", "instructor-before");
        writerRepo.Insert(section);

        using var readerDb = new DatabaseContext(_dbPath);
        var readerRepo = new SectionRepository(readerDb);
        var store = new SectionStore();

        // Prime the cache.
        store.Reload(readerRepo, [semId]);
        Assert.Equal("instructor-before", store.Sections.Single().InstructorAssignments.Single().InstructorId);

        // Writer commits a change.
        section.InstructorAssignments.Clear();
        section.InstructorAssignments.Add(new InstructorAssignment { InstructorId = "instructor-after" });
        writerRepo.Update(section);

        // Reader does NOT call store.Reload() — this simulates calling
        // SectionListViewModel.Reload() (the pre-fix RefreshData path), which
        // rebuilds the view from the existing in-memory cache without touching the DB.
        // The cache still shows the old instructor.
        Assert.True(
            store.Sections.Single().InstructorAssignments.Single().InstructorId == "instructor-before",
            "Cache is stale: writer change is invisible without calling store.Reload().");
    }

    /// <summary>
    /// Verifies that <see cref="SectionStore.SectionsChanged"/> fires only when
    /// <see cref="SectionStore.Reload"/> is explicitly called, and not when the
    /// caller merely reads from the existing cache.
    ///
    /// <para>
    /// This captures the full lifecycle of the bug and its fix:
    /// <list type="number">
    ///   <item>Cache is primed → <c>SectionsChanged</c> fires (count = 1).</item>
    ///   <item>Writer commits a change.</item>
    ///   <item>
    ///     <em>Bug path</em>: caller reads <see cref="SectionStore.Sections"/> without
    ///     calling <c>Reload</c> → event does not fire (count stays at 1), cache is stale.
    ///   </item>
    ///   <item>
    ///     <em>Fix path</em>: caller calls <c>store.Reload(repo, semIds)</c> →
    ///     event fires (count = 2), cache shows updated instructor.
    ///   </item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// In production the fix is: <c>MainWindowViewModel.RefreshData()</c> should call
    /// a method that triggers <c>_sectionStore.Reload(repo, semIds)</c>.  All three
    /// subscribers (section list, schedule grid, workload panel) will then receive
    /// <c>SectionsChanged</c> and rebuild their views automatically.
    /// </para>
    /// </summary>
    [Fact]
    public void SectionStore_SectionsChangedFires_OnlyWhenReloadIsCalled()
    {
        var semId = Guid.NewGuid().ToString();

        using var writerDb = new DatabaseContext(_dbPath);
        InsertSemester(writerDb.Connection, semId);
        var writerRepo = new SectionRepository(writerDb);
        var section = MakeSection(semId, "A1", "instructor-before");
        writerRepo.Insert(section);

        using var readerDb = new DatabaseContext(_dbPath);
        var readerRepo = new SectionRepository(readerDb);
        var store = new SectionStore();

        int changedCount = 0;
        store.SectionsChanged += () => changedCount++;

        // Prime the cache — SectionsChanged fires once.
        store.Reload(readerRepo, [semId]);
        Assert.Equal(1, changedCount);

        // Writer commits a change.
        section.InstructorAssignments.Clear();
        section.InstructorAssignments.Add(new InstructorAssignment { InstructorId = "instructor-after" });
        writerRepo.Update(section);

        // Bug path: simply reading the cache does not trigger SectionsChanged.
        // (Equivalent to SectionListViewModel.Reload() → Load() → LoadCore() reading
        // store.SectionsBySemester without ever calling store.Reload().)
        _ = store.Sections; // read the cache — no event
        Assert.Equal(1, changedCount);
        Assert.True(
            store.Sections.Single().InstructorAssignments.Single().InstructorId == "instructor-before",
            "Cache is still stale after the bug-path read.");

        // Fix path: call store.Reload() — SectionsChanged fires and cache is refreshed.
        store.Reload(readerRepo, [semId]);
        Assert.Equal(2, changedCount);
        Assert.True(
            store.Sections.Single().InstructorAssignments.Single().InstructorId == "instructor-after",
            "Cache is fresh after calling store.Reload().");
    }
}
