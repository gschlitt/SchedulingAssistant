using TermPoint.Models;
using TermPoint.Services.Access;
using TermPoint.ViewModels.Access;
using Xunit;
using static TermPoint.Tests.AccessTestKit;

namespace TermPoint.Tests;

/// <summary>
/// <see cref="AccessText"/>: the hover wording of the conflict marks (round 6, approved), a port of the r9 mock's
/// <c>conflictTips</c>. Marks come from the engine where the scenario matters, and are built by hand where only the
/// wording is under test.
/// </summary>
public sealed class AccessTextTests
{
    private static List<WatchMark> WatchMarks(string watch, IEnumerable<ConflictMark> marks) =>
        marks.Select(m => new WatchMark("id-" + watch, watch, m)).ToList();

    private static IReadOnlyList<ConflictMark> Engine(IReadOnlyList<Section> sections, bool markAll = false)
    {
        var inputs = Simple(sections);
        return ConflictMarks.For(inputs, inputs.Sections, false, markAll);
    }

    private static IReadOnlyList<string> Tips(
        IReadOnlyList<Section> sections, string sectionId, IReadOnlyList<WatchMark> marks,
        int day = 1, Func<string, HiddenReason?>? hidden = null) =>
        AccessText.ConflictTips(Simple(sections), sections.Single(s => s.Id == sectionId), day, marks, hidden);

    private static ConflictMark Hand(ConflictMarkKind kind, Section a, Section b) =>
        new(kind, a, b, SectionOverlap.OverlappingMeetings(a, b), null, []);

    [Fact]
    public void Stranded_OneSection()
    {
        var sections = new[]
        {
            Sec("A", "1", Lecture, At(0, 9, 10)),
            Sec("B", "1", Lecture, At(0, 9, 10)), Sec("B", "2", Lecture, At(1, 9, 10)),
        };
        var marks = WatchMarks("Y2", Engine(sections));
        var expected = "B 1 conflicts with A 1, so B 1 can't be part of any slate for **Y2**.";
        Assert.Equal([expected], Tips(sections, "B 1", marks));
        Assert.Equal([expected], Tips(sections, "A 1", marks));
        Assert.Empty(Tips(sections, "B 2", marks));
        Assert.Empty(Tips(sections, "A 1", marks, day: 2));
    }

    [Fact]
    public void Stranded_SectionsConflictingWithTheSameSectionsAreMerged()
    {
        var sections = new[]
        {
            Sec("A", "1", Lecture, At(0, 9, 10)), Sec("A", "2", Lecture, At(0, 9, 10)), Sec("A", "3", Lecture, At(1, 9, 10)),
            Sec("B", "1", Lecture, At(0, 9, 10)),
        };
        var marks = WatchMarks("Y2", Engine(sections));
        Assert.Equal(["A 1, 2 conflict with B 1, so they can't be part of any slate for **Y2**."], Tips(sections, "B 1", marks));
    }

    [Fact]
    public void Move_OneConflict_NamesThePartnerAndEitherSectionToMove()
    {
        var sections = new[] { Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(0, 9, 10)) };
        var marks = WatchMarks("Y2", Engine(sections));
        Assert.Equal(["A 1 conflicts with B 1, so **Y2** has no full slate. Try moving A 1 or B 1."],
            Tips(sections, "A 1", marks));
    }

    [Fact]
    public void Move_Q22_OneSectionClashingWithTwo_IsOneMove()
    {
        var sections = new[]
        {
            Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(1, 9, 10)),
            Sec("C", "1", Lecture, At([0, 1], 9, 10)),
        };
        var marks = WatchMarks("Y2", Engine(sections));
        Assert.Equal(["A 1 conflicts with C 1, so **Y2** has no full slate. Try moving C 1."],
            Tips(sections, "A 1", marks));
        Assert.Equal(["C 1 conflicts with A 1, so **Y2** has no full slate. Try moving C 1."],
            Tips(sections, "C 1", marks));
        Assert.Equal(["C 1 conflicts with B 1, so **Y2** has no full slate. Try moving C 1."],
            Tips(sections, "C 1", marks, day: 2));
    }

    [Fact]
    public void Move_TwoSeparateProblems_SaysWhichOneThisIs()
    {
        var sections = new[]
        {
            Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(0, 9, 10)),
            Sec("C", "1", Lecture, At(1, 9, 10)), Sec("D", "1", Lecture, At(1, 9, 10)),
        };
        var marks = WatchMarks("Y2", Engine(sections));
        Assert.Equal(["A 1 conflicts with B 1. This is one of 2 separate problems keeping **Y2** from a full slate; moving A 1 or B 1 fixes it."],
            Tips(sections, "A 1", marks));
    }

    [Fact]
    public void ByCourseOr_GivesEachCourseCodeOnce()
    {
        var sections = new[] { Sec("A", "1"), Sec("A", "2"), Sec("B", "1") };
        Assert.Equal("A 1, 2 or B 1", AccessText.ByCourseOr(Simple(sections), sections));
    }

    [Fact]
    public void Other_NamesTheFreedSections()
    {
        var sections = AccessAnalyzerTests.ThreeCourseChain();
        var mark = Engine(sections).Single(m => m.Kind == ConflictMarkKind.Other);
        var marks = WatchMarks("Y2", [mark]);
        var line = Assert.Single(Tips(sections, mark.A.Id, marks, day: mark.Meetings[0].A.Day));
        Assert.Equal($"{mark.A.Id} conflicts with {mark.B.Id}. Because of this conflict, B x can't be part of any slate for **Y2**.", line);
    }

    [Fact]
    public void Harmless_SaysOtherSectionsLetStudentsGetAround_SingularAndPlural()
    {
        var a1 = Sec("A", "1", Lecture, At(0, 9, 10));
        var b1 = Sec("B", "1", Lecture, At(0, 9, 10));
        var c1 = Sec("C", "1", Lecture, At(0, 9, 10));
        var sections = new[] { a1, b1, c1 };

        var one = WatchMarks("Y2", [Hand(ConflictMarkKind.Harmless, a1, b1)]);
        Assert.Equal(["A 1 conflicts with B 1. Other sections still let students in **Y2** get around this conflict."],
            Tips(sections, "A 1", one));

        var two = WatchMarks("Y2", [Hand(ConflictMarkKind.Harmless, a1, b1), Hand(ConflictMarkKind.Harmless, a1, c1)]);
        Assert.Equal(["A 1 conflicts with B 1 and C 1. Other sections still let students in **Y2** get around these conflicts."],
            Tips(sections, "A 1", two));
    }

    [Fact]
    public void Plain_NamesTheConflictsAndClaimsNothing()
    {
        var a1 = Sec("A", "1", Lecture, At(0, 9, 10));
        var b1 = Sec("B", "1", Lecture, At(0, 9, 10));
        var c1 = Sec("C", "1", Lecture, At(0, 9, 10));
        var marks = WatchMarks("Y2", [Hand(ConflictMarkKind.Plain, a1, b1), Hand(ConflictMarkKind.Plain, a1, c1)]);
        Assert.Equal(["A 1 conflicts with B 1 and C 1."], Tips([a1, b1, c1], "A 1", marks));
    }

    [Fact]
    public void ByCourse_GroupsSectionCodesUnderTheirCourse()
    {
        var sections = new[]
        {
            Sec("PSYCH 101", "A", Lecture), Sec("PSYCH 101", "B", Lecture), Sec("GEOG 250", "A", Lecture),
            Sec("PSYCH 101", "C", Lecture),
        };
        Assert.Equal("PSYCH 101 A, B, C and GEOG 250 A",
            AccessText.ByCourse(Simple(sections), [sections[0], sections[1], sections[3], sections[2]]));
    }

    [Fact]
    public void JoinAnd_HandlesNoneOneTwoAndMany()
    {
        Assert.Equal("", AccessText.JoinAnd([]));
        Assert.Equal("a", AccessText.JoinAnd(["a"]));
        Assert.Equal("a and b", AccessText.JoinAnd(["a", "b"]));
        Assert.Equal("a, b and c", AccessText.JoinAnd(["a", "b", "c"]));
    }

    [Fact]
    public void SeveralWatches_EachLineIsLedByItsWatchName_OneWatchHasNoPrefix()
    {
        var sections = new[] { Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(0, 9, 10)) };
        var mark = Engine(sections).Single();
        var both = WatchMarks("Y2", [mark]).Concat(WatchMarks("Y3", [mark])).ToList();
        Assert.Equal(
            ["**Y2**: A 1 conflicts with B 1, so **Y2** has no full slate. Try moving A 1 or B 1.", "**Y3**: A 1 conflicts with B 1, so **Y3** has no full slate. Try moving A 1 or B 1."],
            Tips(sections, "A 1", both));
        Assert.Equal(["A 1 conflicts with B 1, so **Y2** has no full slate. Try moving A 1 or B 1."],
            Tips(sections, "A 1", WatchMarks("Y2", [mark])));
    }

    [Fact]
    public void HiddenPartner_GetsOneExtraLine_PerPartner()
    {
        var sections = new[] { Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(0, 9, 10)) };
        var marks = WatchMarks("Y2", Engine(sections));
        var lines = Tips(sections, "A 1", marks, hidden: id => id == "B 1" ? HiddenReason.Filters : null);
        Assert.Equal(["A 1 conflicts with B 1, so **Y2** has no full slate. Try moving A 1 or B 1.", "B 1 is hidden by the filters."], lines);
        Assert.Equal(["A 1 conflicts with B 1, so **Y2** has no full slate. Try moving A 1 or B 1."], Tips(sections, "A 1", marks, hidden: _ => null));
    }

    /// <summary>A partner left out by the chosen subjects (not the filters) says so (Greg, 2026-11-24).</summary>
    [Fact]
    public void PartnerOutsideTheChosenSubjects_SaysSo()
    {
        var sections = new[] { Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(0, 9, 10)) };
        var marks = WatchMarks("Y2", Engine(sections));
        var lines = Tips(sections, "A 1", marks, hidden: id => id == "B 1" ? HiddenReason.Subjects : null);
        Assert.Equal(["A 1 conflicts with B 1, so **Y2** has no full slate. Try moving A 1 or B 1.",
                      "B 1 isn't shown: its subject isn't one of your chosen Subjects."], lines);
    }

    /// <summary>A section of a course left out for a missing component says which components, with the watch in bold.</summary>
    [Fact]
    public void MissingComponentTip_NamesTheMissingComponents_AndBoldsTheWatch()
    {
        Assert.Equal("MANU330 L0A isn't counted: MANU330's lecture sections aren't in **CHBE-Y2**.",
            AccessText.MissingComponentTip("MANU330 L0A", "MANU330", ["lecture"], AccessText.Bold("CHBE-Y2")));
        Assert.Equal("**AB**", AccessText.Bold("A*B"));
    }
}
