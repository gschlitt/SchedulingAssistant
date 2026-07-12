using System.Diagnostics;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests;

/// <summary>
/// Phase D of the July-2026 lockup audit: measures the bulk database operations that
/// run synchronously on the UI thread, using a temp copy of the real-data database
/// (<see cref="RoomAvailabilityIntegrationTests.SourceDbPath"/>). The goal is to decide
/// empirically whether any of them need to move off-thread behind a progress UI.
///
/// <para>Operations measured (mirroring their production call paths):</para>
/// <list type="bullet">
///   <item>Copy Semester — clone every section of the busiest semester into a new
///         semester inside one transaction (<c>CopySemesterViewModel.ExecuteCopy</c>).</item>
///   <item>Tag-style cascade — read all sections and rewrite each one inside one
///         transaction, the worst case of
///         <c>SchedulingEnvironmentListViewModel.Delete</c>.</item>
///   <item>Empty Semester — <c>DeleteBySemesterId</c> single-statement bulk delete.</item>
///   <item>Section list reload — <c>GetAll()</c> full-table read that backs
///         <c>SectionListViewModel.LoadCore</c>.</item>
/// </list>
///
/// <para>The assertions use a deliberately generous 2-second ceiling: they are a
/// tripwire for order-of-magnitude regressions (e.g. a per-row transaction sneaking
/// in), not a micro-benchmark. Timings are written to the test output for the audit
/// record. Skipped automatically on machines without the private real-data DB.</para>
/// </summary>
public class BulkDbWorkTimingTests : IDisposable
{
    private const int CeilingMs = 2000;

    private readonly string _tempDbPath;
    private readonly DatabaseContext _db;
    private readonly ITestOutputHelper _output;

    private readonly SemesterRepository _semRepo;
    private readonly SectionRepository _sectionRepo;
    private readonly Semester _busiestSemester;
    private readonly List<Section> _busiestSections;

    public BulkDbWorkTimingTests(ITestOutputHelper output)
    {
        _output = output;

        _tempDbPath = Path.Combine(Path.GetTempPath(), $"BulkTimingTest_{Guid.NewGuid():N}.db");
        File.Copy(RoomAvailabilityIntegrationTests.SourceDbPath, _tempDbPath, overwrite: true);

        _db = new DatabaseContext(_tempDbPath);
        var ayRepo   = new AcademicYearRepository(_db);
        _semRepo     = new SemesterRepository(_db);
        _sectionRepo = new SectionRepository(_db);

        // Find the busiest semester — the worst case for every bulk operation.
        Semester? best = null;
        int bestCount = 0;
        foreach (var ay in ayRepo.GetAll())
        {
            foreach (var sem in _semRepo.GetByAcademicYear(ay.Id))
            {
                int count = _sectionRepo.GetAll(sem.Id).Count;
                if (count > bestCount) { bestCount = count; best = sem; }
            }
        }
        _busiestSemester = best ?? throw new InvalidOperationException("No semester with sections found.");
        _busiestSections = _sectionRepo.GetAll(_busiestSemester.Id);
    }

    [FactRequiresLocalDb]
    public void CopySemester_BulkInsert_CompletesWithinCeiling()
    {
        // Mirror CopySemesterViewModel: create the target semester, then clone every
        // source section into it inside a single transaction.
        var target = new Semester
        {
            Id             = Guid.NewGuid().ToString("N"),
            AcademicYearId = _busiestSemester.AcademicYearId,
            Name           = "TimingCopyTarget",
        };
        _semRepo.Insert(target);

        var sw = Stopwatch.StartNew();
        using (var tx = _db.Connection.BeginTransaction())
        {
            foreach (var source in _busiestSections)
            {
                var clone = new Section
                {
                    SemesterId  = target.Id,
                    CourseId    = source.CourseId,
                    SectionCode = source.SectionCode,
                    CampusId    = source.CampusId,
                    SectionTypeId = source.SectionTypeId,
                    TagIds      = new List<string>(source.TagIds),
                    Schedule    = source.Schedule,
                };
                _sectionRepo.Insert(clone, tx);
            }
            tx.Commit();
        }
        sw.Stop();

        _output.WriteLine($"Copy Semester: {_busiestSections.Count} sections inserted in {sw.ElapsedMilliseconds} ms");
        Assert.True(sw.ElapsedMilliseconds < CeilingMs,
            $"Copy Semester took {sw.ElapsedMilliseconds} ms for {_busiestSections.Count} sections (ceiling {CeilingMs} ms)");
    }

    [FactRequiresLocalDb]
    public void TagCascade_UpdateEverySection_CompletesWithinCeiling()
    {
        // Worst case of SchedulingEnvironmentListViewModel.Delete: every section in the
        // database is touched and rewritten inside one transaction.
        var all = _sectionRepo.GetAll();

        var sw = Stopwatch.StartNew();
        using (var tx = _db.Connection.BeginTransaction())
        {
            foreach (var section in all)
                _sectionRepo.Update(section, tx);
            tx.Commit();
        }
        sw.Stop();

        _output.WriteLine($"Tag cascade: {all.Count} sections rewritten in {sw.ElapsedMilliseconds} ms");
        Assert.True(sw.ElapsedMilliseconds < CeilingMs,
            $"Tag cascade took {sw.ElapsedMilliseconds} ms for {all.Count} sections (ceiling {CeilingMs} ms)");
    }

    [FactRequiresLocalDb]
    public void EmptySemester_DeleteBySemesterId_CompletesWithinCeiling()
    {
        var sw = Stopwatch.StartNew();
        _sectionRepo.DeleteBySemesterId(_busiestSemester.Id);
        sw.Stop();

        _output.WriteLine($"Empty Semester: {_busiestSections.Count} sections deleted in {sw.ElapsedMilliseconds} ms");
        Assert.True(sw.ElapsedMilliseconds < CeilingMs,
            $"DeleteBySemesterId took {sw.ElapsedMilliseconds} ms for {_busiestSections.Count} sections (ceiling {CeilingMs} ms)");
    }

    [FactRequiresLocalDb]
    public void SectionList_FullTableRead_CompletesWithinCeiling()
    {
        var sw = Stopwatch.StartNew();
        var all = _sectionRepo.GetAll();
        sw.Stop();

        _output.WriteLine($"Full section read: {all.Count} sections in {sw.ElapsedMilliseconds} ms");
        Assert.True(sw.ElapsedMilliseconds < CeilingMs,
            $"GetAll took {sw.ElapsedMilliseconds} ms for {all.Count} sections (ceiling {CeilingMs} ms)");
    }

    public void Dispose()
    {
        _db.Dispose();
        // WAL sidecars are created because DatabaseContext opens working copies in WAL mode.
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_tempDbPath + suffix); } catch { /* best effort */ }
        }
    }
}
