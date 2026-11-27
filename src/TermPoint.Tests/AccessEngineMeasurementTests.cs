using System.Diagnostics;
using System.IO;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services.Access;
using TermPoint.ViewModels.Management;
using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests;

/// <summary>
/// Runs a test only when the UBC Engineering demo database (<see cref="AccessEngineMeasurementTests.SourceDbPath"/>)
/// exists on this machine; elsewhere (including CI) it is reported as skipped.
/// </summary>
public sealed class FactRequiresUbcDemoDbAttribute : FactAttribute
{
    /// <summary>Sets <see cref="FactAttribute.Skip"/> when the database file is missing.</summary>
    public FactRequiresUbcDemoDbAttribute()
    {
        if (!File.Exists(AccessEngineMeasurementTests.SourceDbPath))
            Skip = $"UBC demo DB not found at '{AccessEngineMeasurementTests.SourceDbPath}'.";
    }
}

/// <summary>
/// Access Analyzer piece C.3: times the engine on real data (UBC Engineering, Winter Term 1, with its program-year
/// tags treated as watched tags) against the 30 ms budget for recomputing every watch after a save (Phase 5,
/// Performance). Works on a temporary copy of the database, never the original. Writes its numbers to the test
/// output and to <c>%TEMP%\access-engine-measurement.txt</c>; it fails only if the engine throws or a single
/// watch takes longer than a second (a sign of a runaway search, not of a missed budget).
/// </summary>
public sealed class AccessEngineMeasurementTests(ITestOutputHelper output)
{
    /// <summary>
    /// The source database (UBC Engineering scrape with program-year tags). Only ever copied. The environment variable
    /// <c>TERMPOINT_ACCESS_MEASURE_DB</c> points it at another copy (machines keep the UBC data in different places).
    /// </summary>
    internal static readonly string SourceDbPath =
        Environment.GetEnvironmentVariable("TERMPOINT_ACCESS_MEASURE_DB") is { Length: > 0 } path
            ? path : @"C:\ProgramData\TermPoint\DEMO\Test-TT.db";

    private const int Runs = 15;

    /// <summary>
    /// Loads the semester with the most sections, then times: each tag as a watch (analysis, and marks with Mark
    /// all conflicts off and on), every watch at once, a union watch of every tag (the worst plausible watch), and
    /// Check all courses. Reports the median of <see cref="Runs"/> runs after a warm-up.
    /// </summary>
    [FactRequiresUbcDemoDb]
    public void MeasureEngineOnUbcEngineering()
    {
        string temp = Path.Combine(Path.GetTempPath(), $"access-measure-{Guid.NewGuid():N}.db");
        File.Copy(SourceDbPath, temp);
        var lines = new List<string>();
        void Say(string s) { lines.Add(s); output.WriteLine(s); }
        try
        {
            AccessInputs inputs;
            List<SchedulingEnvironmentValue> tags;
            var load = Stopwatch.StartNew();
            using (var db = new DatabaseContext(temp))
            {
                var sectionRepo = new SectionRepository(db);
                var semester = new SemesterRepository(db).GetAll()
                    .OrderByDescending(s => sectionRepo.CountBySemesterId(s.Id)).First();
                var env = new SchedulingEnvironmentRepository(db);
                tags = env.GetAll(SchedulingEnvironmentTypes.Tag);
                var suggestion = Time(() => sectionRepo.GetSectionTypeIdsByCourse(), out double suggestionMs);
                inputs = new AccessInputs(
                    sectionRepo.GetAll(semester.Id),
                    new CourseRepository(db).GetAll().ToDictionary(c => c.Id),
                    env.GetAll(SchedulingEnvironmentTypes.SectionType),
                    new AccessChecksSetupRepository(db).IsSetUp(),
                    suggestion);
                Say($"Semester {semester.Name}: {inputs.Sections.Count} sections, {inputs.SectionsByCourse.Count} courses, " +
                    $"{tags.Count} tags ({tags.Count(t => t.IsAccessWatch)} converted watches); set up: {inputs.SetUp}; " +
                    $"open + migrate + load {load.ElapsedMilliseconds} ms; suggestion query {suggestionMs:F1} ms");
            }

            var watches = tags.Select(t => (Name: t.Name, Watch: new AccessWatch(t.Id, t.WatchMultiCampus)))
                .Where(w => w.Watch.CoveredSections(inputs).Count > 0).ToList();

            Say("");
            Say("watch | courses | groups | sections | slates | health | analyse ms | marks ms | marks+all ms");
            double worst = 0;
            foreach (var (name, watch) in watches.OrderByDescending(w => w.Watch.CoveredSections(inputs).Count))
            {
                var a = AccessAnalyzer.Analyze(inputs, watch);
                double ta = Median(() => AccessAnalyzer.Analyze(inputs, watch));
                double tm = Median(() => ConflictMarks.For(inputs, watch, markAll: false));
                double tall = Median(() => ConflictMarks.For(inputs, watch, markAll: true));
                worst = Math.Max(worst, ta + tm);
                Say($"{name} | {a.OfferedCourseIds.Count} | {a.Groups.Count} | {watch.CoveredSections(inputs).Count} | " +
                    $"{a.SlateCount}{(a.SlateCountCapped ? "+" : "")} | {a.Health} | {ta:F2} | {tm:F2} | {tall:F2}");
                Assert.True(ta + tm < 1000, $"{name} took {ta + tm:F0} ms");
            }

            double every = Median(() =>
            {
                foreach (var (_, w) in watches) { AccessAnalyzer.Analyze(inputs, w); ConflictMarks.For(inputs, w, false); }
            });

            var check = CheckAllCourses.Run(inputs);
            double tCheck = Median(() => CheckAllCourses.Run(inputs));

            Say("");
            Say($"Every watch ({watches.Count}), analyse + marks: {every:F2} ms (budget 30 ms); worst single watch {worst:F2} ms");
            Say($"Check all courses ({check.OfferedCourseCount} courses, {check.MultiComponentCourseCount} with 2+ components; " +
                $"{check.Problems.Count} problems, {check.Quiet.Count} quiet): {tCheck:F2} ms");

            // Larger watches than any program has: the sections of the first k tagged courses (by code), growing
            // until one step passes a second (the best partial's subset search is exponential in courses).
            Say("");
            Say("union of k courses | groups | sections | slates | best covers | analyse ms | marks ms");
            var taggedCourses = inputs.Sections.Where(s => s.TagIds.Count > 0 && s.CourseId is not null)
                .Select(s => s.CourseId!).Distinct().OrderBy(c => c, inputs.CourseOrder).ToList();
            for (int k = 6; k <= taggedCourses.Count; k += 2)
            {
                var ids = taggedCourses.Take(k).ToHashSet();
                var covered = inputs.Sections.Where(s => s.CourseId is not null && ids.Contains(s.CourseId)).ToList();
                var a = Time(() => AccessAnalyzer.Analyze(inputs, covered, false, null, null), out double ta);
                Time(() => ConflictMarks.For(inputs, covered, false, false), out double tm);
                Say($"{k} | {a.Groups.Count} | {covered.Count} | {a.SlateCount}{(a.SlateCountCapped ? "+" : "")} | " +
                    $"{a.BestPartial?.Covers.ToString() ?? "-"} | {ta:F1} | {tm:F1}");
                if (ta + tm > 1000) break;
            }
        }
        finally
        {
            File.WriteAllLines(Path.Combine(Path.GetTempPath(), "access-engine-measurement.txt"), lines);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var f in new[] { temp, temp + "-wal", temp + "-shm" })
                try { File.Delete(f); } catch { /* best effort */ }
        }
    }

    /// <summary>Runs <paramref name="work"/> once and returns its result and elapsed milliseconds.</summary>
    private static T Time<T>(Func<T> work, out double ms)
    {
        var sw = Stopwatch.StartNew();
        var r = work();
        ms = sw.Elapsed.TotalMilliseconds;
        return r;
    }

    /// <summary>The median elapsed milliseconds of <paramref name="runs"/> runs, after one warm-up run.</summary>
    private static double Median(Action work, int runs = Runs)
    {
        work();
        var times = new double[runs];
        for (int i = 0; i < runs; i++)
        {
            var sw = Stopwatch.StartNew();
            work();
            times[i] = sw.Elapsed.TotalMilliseconds;
        }
        Array.Sort(times);
        return times[runs / 2];
    }

    /// <summary>Overload for work that returns a value (the value is discarded).</summary>
    private static double Median<T>(Func<T> work, int runs = Runs) => Median(() => { work(); }, runs);
}
