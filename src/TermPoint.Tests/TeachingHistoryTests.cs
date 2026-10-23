using System.Linq;
using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Pure tests of <see cref="TeachingHistory.Build"/> (spec item 20, step 20.1): who is credited with which
/// subject in which academic year, what is skipped, and how academic years are ordered.
/// </summary>
public sealed class TeachingHistoryTests
{
    /// <summary>The subjects an instructor taught in one named year, sorted; empty when they taught nothing then.</summary>
    private static string[] SubjectsIn(TeachingHistory history, FocusWorld world, string instructorId, string yearName)
    {
        var index = history.GetYearIndex(world.YearId(yearName));
        if (index is null) return [];
        return history.GetSubjectsByYear(instructorId).TryGetValue(index.Value, out var subjects)
            ? subjects.OrderBy(s => s).ToArray()
            : [];
    }

    /// <summary>A section with two instructors counts for both of them.</summary>
    [Fact]
    public void MultiInstructorSection_CountsForEveryInstructor()
    {
        var w = new FocusWorld();
        w.Teach("BIOL", "2025-2026", ["a", "b"]);
        var h = w.Build();

        Assert.Equal(["BIOL"], SubjectsIn(h, w, "a", "2025-2026"));
        Assert.Equal(["BIOL"], SubjectsIn(h, w, "b", "2025-2026"));
    }

    /// <summary>The same instructor listed twice on one section is one credit, not an error.</summary>
    [Fact]
    public void InstructorListedTwice_IsCountedOnce()
    {
        var w = new FocusWorld();
        w.Teach("BIOL", "2025-2026", ["a", "a"]);
        Assert.Equal(["BIOL"], SubjectsIn(w.Build(), w, "a", "2025-2026"));
    }

    /// <summary>One instructor's subjects are collected per year, across semesters and sections.</summary>
    [Fact]
    public void SubjectsAreCollectedPerYear_AcrossSemesters()
    {
        var w = new FocusWorld();
        w.Teach("a", "BIOL", "2025-2026", "Fall");
        w.Teach("a", "ENGL", "2025-2026", "Winter");
        w.Teach("a", "CHEM", "2024-2025");
        var h = w.Build();

        Assert.Equal(["BIOL", "ENGL"], SubjectsIn(h, w, "a", "2025-2026"));
        Assert.Equal(["CHEM"], SubjectsIn(h, w, "a", "2024-2025"));
    }

    /// <summary>A section whose course is unknown, or that has no course id, is skipped.</summary>
    [Fact]
    public void UnknownOrMissingCourse_IsSkipped()
    {
        var w = new FocusWorld();
        w.Teach("a", "BIOL", "2025-2026");
        w.Sections.Add(new Section
        {
            Id = "orphan-course", CourseId = "no-such-course", SemesterId = w.Semester("2025-2026").Id,
            InstructorAssignments = [new InstructorAssignment { InstructorId = "b" }],
        });
        w.Sections.Add(new Section
        {
            Id = "no-course", CourseId = null, SemesterId = w.Semester("2025-2026").Id,
            InstructorAssignments = [new InstructorAssignment { InstructorId = "c" }],
        });
        var h = w.Build();

        Assert.Equal(["BIOL"], SubjectsIn(h, w, "a", "2025-2026"));
        Assert.Empty(h.GetSubjectsByYear("b"));
        Assert.Empty(h.GetSubjectsByYear("c"));
    }

    /// <summary>A section whose semester is unknown, or whose semester names an unknown academic year, is skipped.</summary>
    [Fact]
    public void UnknownSemesterOrYear_IsSkipped()
    {
        var w = new FocusWorld();
        w.Teach("a", "BIOL", "2025-2026");
        w.CourseOf("ENGL");
        w.Sections.Add(new Section
        {
            Id = "orphan-semester", CourseId = w.CourseOf("ENGL").Id, SemesterId = "no-such-semester",
            InstructorAssignments = [new InstructorAssignment { InstructorId = "b" }],
        });
        // A semester pointing at a year that is not in the list given to Build.
        w.Semesters["x/Fall"] = new Semester { Id = "sem-x", AcademicYearId = "ay-missing", Name = "Fall" };
        w.Sections.Add(new Section
        {
            Id = "orphan-year", CourseId = w.CourseOf("ENGL").Id, SemesterId = "sem-x",
            InstructorAssignments = [new InstructorAssignment { InstructorId = "c" }],
        });
        var h = w.Build();

        Assert.Equal(["BIOL"], SubjectsIn(h, w, "a", "2025-2026"));
        Assert.Empty(h.GetSubjectsByYear("b"));
        Assert.Empty(h.GetSubjectsByYear("c"));
    }

    /// <summary>A course with an empty subject id still makes the section count as "taught something" that year.</summary>
    [Fact]
    public void CourseWithEmptySubject_StillRecordsTheYear()
    {
        var w = new FocusWorld();
        w.Teach("a", "", "2025-2026");
        var h = w.Build();
        Assert.Single(h.GetSubjectsByYear("a"));
    }

    /// <summary>An instructor who has taught nothing gets an empty answer, not an error.</summary>
    [Fact]
    public void UnknownInstructor_HasNoYears()
    {
        Assert.Empty(new FocusWorld().Build().GetSubjectsByYear("nobody"));
        Assert.Empty(TeachingHistory.Empty.GetSubjectsByYear("nobody"));
    }

    /// <summary>Years are positioned by start year, not by the order they were supplied in.</summary>
    [Fact]
    public void YearsAreOrderedByStartYear()
    {
        var w = new FocusWorld();
        w.Year("2025-2026");
        w.Year("2019-2020");
        w.Year("2022-2023");
        var h = w.Build();

        Assert.Equal(0, h.GetYearIndex(w.YearId("2019-2020")));
        Assert.Equal(1, h.GetYearIndex(w.YearId("2022-2023")));
        Assert.Equal(2, h.GetYearIndex(w.YearId("2025-2026")));
    }

    /// <summary>
    /// Names that do not parse share start year int.MaxValue, so they sort after every real year and, among
    /// themselves, by name (ordinal).
    /// </summary>
    [Fact]
    public void UnparseableNames_SortLast_ThenByName()
    {
        var w = new FocusWorld();
        w.Year("Zeta");
        w.Year("2025-2026");
        w.Year("Alpha");
        var h = w.Build();

        Assert.Equal(0, h.GetYearIndex(w.YearId("2025-2026")));
        Assert.Equal(1, h.GetYearIndex(w.YearId("Alpha")));
        Assert.Equal(2, h.GetYearIndex(w.YearId("Zeta")));
    }

    /// <summary>Two years with the same start year are ordered by name (ordinal).</summary>
    [Fact]
    public void SameStartYear_OrderedByName()
    {
        var w = new FocusWorld();
        w.Year("2025-2027");
        w.Year("2025-2026");
        var h = w.Build();

        Assert.Equal(0, h.GetYearIndex(w.YearId("2025-2026")));
        Assert.Equal(1, h.GetYearIndex(w.YearId("2025-2027")));
    }

    /// <summary>A null or unknown academic year id has no position.</summary>
    [Fact]
    public void GetYearIndex_NullOrUnknown_IsNull()
    {
        var h = new FocusWorld().Build();
        Assert.Null(h.GetYearIndex(null));
        Assert.Null(h.GetYearIndex("nope"));
        Assert.Null(TeachingHistory.Empty.GetYearIndex("anything"));
    }
}
