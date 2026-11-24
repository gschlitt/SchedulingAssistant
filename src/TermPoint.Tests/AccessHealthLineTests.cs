using TermPoint.Models;
using TermPoint.Services.Access;
using TermPoint.ViewModels.Access;
using Xunit;
using static TermPoint.Tests.AccessTestKit;

namespace TermPoint.Tests;

/// <summary>
/// <see cref="AccessText.HealthLine"/>: the Access tab's one-line health of a watch, a port of the r9 mock's
/// <c>healthSegs</c> without seats, cautions, pins or subsets. Real analyses where the scenario matters, hand-built
/// analyses where only a phrase is under test.
/// </summary>
public sealed class AccessHealthLineTests
{
    private static string Campus(string? id) => id switch { "ch" => "Chilliwack", "ab" => "Abbotsford", _ => "another campus" };

    private static string Line(IReadOnlyList<Section> sections, params Course[] courses) =>
        AccessText.HealthLine(Simple(sections, courses), AnalyzeAll(Simple(sections, courses)), Campus);

    private static string Line(AccessInputs inputs, Analysis a) => AccessText.HealthLine(inputs, a, Campus);

    private static AccessInputs Four() => Simple(
        [Sec("A", "1", Lecture), Sec("B", "1", Lecture), Sec("C", "1", Lecture), Sec("D", "1", Lecture)]);

    [Fact]
    public void Slates_AreCounted()
    {
        var sections = new[]
        {
            Sec("A", "1", Lecture, At(0, 9, 10)), Sec("A", "2", Lecture, At(1, 9, 10)),
            Sec("B", "1", Lecture, At(2, 9, 10)),
        };
        Assert.Equal("2 slates", Line(sections));
    }

    [Fact]
    public void SlateCountAtTheCap_SaysMoreThan()
    {
        var inputs = Four();
        var group = new ComponentGroup("A", null, [Sec("A", "1", Lecture), Sec("A", "2", Lecture)], new HashSet<string>());
        var a = new Analysis { Health = AccessHealth.Ok, Groups = [group, group], SlateCount = 10000, SlateCountCapped = true, OneCampusRule = true };
        Assert.Equal("More than 10,000 slates", Line(inputs, a));
    }

    [Fact]
    public void DependsOnStrandedAndConflicts_AreNamed()
    {
        var sections = new[]
        {
            Sec("A", "1", Lecture, At(0, 9, 10)), Sec("A", "2", Lecture, At(1, 9, 10)),
            Sec("B", "1", Lecture, At(0, 9, 10)),
        };
        Assert.Equal("1 slate · depends on A 2 · 1 section stranded · 1 conflict", Line(sections));
    }

    [Fact]
    public void DependsOn_ListsTwoAndCountsTheRest()
    {
        var inputs = Four();
        var group = new ComponentGroup("A", null, [Sec("A", "1", Lecture), Sec("A", "2", Lecture)], new HashSet<string>());
        var a = new Analysis
        {
            Health = AccessHealth.Fragile, Groups = [group, group], SlateCount = 1, OneCampusRule = true,
            EssentialByTimingSections = [Sec("A", "1", Lecture), Sec("B", "1", Lecture), Sec("C", "1", Lecture)],
        };
        Assert.Equal("1 slate · depends on A 1, B 1 +1 more", Line(inputs, a));
    }

    [Fact]
    public void NoFullSlate_NamesTheBestPartialAndWhatCannotFit()
    {
        var sections = new[]
        {
            Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(0, 9, 10)),
            Sec("C", "1", Lecture, At(1, 9, 10)), Sec("C", "2", Lecture, At(2, 9, 10)),
        };
        Assert.Equal("No full slate · best partial covers 2 of 3 · A or B can't fit · 1 conflict", Line(sections));
    }

    [Theory]
    [InlineData("A,B", "A and B can't fit")]
    [InlineData("A;B", "A or B can't fit")]
    [InlineData("A,B;A,C;B,C", "only one of A, B and C fits")]
    [InlineData("A,B;C,D", "2 of A, B, C and D can't fit")]
    public void BestPartial_PhrasesTheAlternatives(string alternatives, string expected)
    {
        var alts = alternatives.Split(';').Select(x => (IReadOnlyList<string>)x.Split(',')).ToList();
        var a = new Analysis
        {
            Health = AccessHealth.Attention,
            BestPartial = new BestPartial(2, 3, alts, alts.SelectMany(x => x).Distinct().ToList()),
        };
        Assert.Equal($"No full slate · best partial covers 2 of 3 · {expected}", Line(Four(), a));
    }

    [Fact]
    public void BestPartial_WhenTheSearchWasBounded_SaysAtLeast()
    {
        var a = new Analysis
        {
            Health = AccessHealth.Attention,
            BestPartial = new BestPartial(2, 3, [["A"]], ["A"], Complete: false),
        };
        Assert.Equal("No full slate · best partial covers at least 2 of 3 · A can't fit", Line(Four(), a));
    }

    [Fact]
    public void SelfFail_NamesTheCourseThatCannotBeTakenAlone()
    {
        var x = Crs("X", Lecture, Lab);
        var sections = new[]
        {
            Sec("X", "101", Lecture, At(0, 9, 10)),
            Sec("X", "L1", Lab, At(0, 9, 10)), Sec("X", "L2", Lab, At(0, 9.5, 10.5)),
            Sec("Y", "1", Lecture, At(1, 9, 10)), Sec("Y", "2", Lecture, At(2, 9, 10)),
        };
        var line = Line(sections, x);
        Assert.StartsWith("No full slate · X's labs all conflict with its only lecture (101)", line);
    }

    [Fact]
    public void Campus_NamesTheOnlySectionsOnEachCampus()
    {
        var inputs = Four();
        var a = new Analysis
        {
            Health = AccessHealth.Attention,
            CampusReason = new CampusReason(CampusReasonKind.OnlySectionsOnDifferentCampuses,
                "ch", [Sec("A", "1", Lecture)], "ab", [Sec("B", "1", Lecture)]),
        };
        Assert.Equal("No full slate · A 1 is at Chilliwack, but B 1 is at Abbotsford", Line(inputs, a));
    }

    [Fact]
    public void Campus_WithoutOnlySections_SaysTheSlatesMixCampuses()
    {
        var a = new Analysis
        {
            Health = AccessHealth.Attention,
            CampusReason = new CampusReason(CampusReasonKind.NoSlateKeepsToOneCampus, null, [], null, []),
        };
        Assert.Equal("No full slate · the slates that fit in time mix campuses", Line(Four(), a));
    }

    [Fact]
    public void MultiCampus_ShowsMixedSlatesAndTheCampusReason()
    {
        var group = new ComponentGroup("A", null, [Sec("A", "1", Lecture), Sec("A", "2", Lecture)], new HashSet<string>());
        var some = new Analysis { Health = AccessHealth.Ok, Groups = [group, group], SlateCount = 3, MixedSlateCount = 2 };
        Assert.Equal("3 slates · 2 mix campuses", Line(Four(), some));

        var all = new Analysis
        {
            Health = AccessHealth.Fragile, Groups = [group, group], SlateCount = 3, MixedSlateCount = 3,
            MixedCampusReason = new CampusReason(CampusReasonKind.NoSlateKeepsToOneCampus, null, [], null, []),
        };
        Assert.Equal("3 slates · every slate mixes campuses · no slate keeps to one campus", Line(Four(), all));
    }

    [Fact]
    public void MissingComponent_SaysWhetherTheTypeIsInTheWatchOrHasNoSections()
    {
        var x = Crs("X", Lecture, Lab);
        var withoutLab = new[]
        {
            Sec("X", "101", Lecture, At(0, 9, 10)),
            Sec("Y", "1", Lecture, At(1, 9, 10)), Sec("Y", "2", Lecture, At(2, 9, 10)),
        };
        Assert.Equal("No full slate · X has no labs this term · 2 slates without X", Line(withoutLab, x));

        // The labs exist in the semester but the watch does not cover them.
        var all = withoutLab.Append(Sec("X", "L1", Lab, At(3, 9, 10))).ToList();
        var inputs = Simple(all, x);
        var a = AccessAnalyzer.Analyze(inputs, withoutLab, false, null, null);
        Assert.Equal("No full slate · X's labs aren't in this watch · 2 slates without X", Line(inputs, a));
    }

    [Fact]
    public void OneSectionCase_SaysWhetherTheSectionsFit()
    {
        Assert.Equal("1 section", Line([Sec("A", "1", Lecture, At(0, 9, 10))]));
        Assert.Equal("Both sections fit together",
            Line([Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(1, 9, 10))]));
        Assert.Equal("All 3 sections fit together",
            Line([Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(1, 9, 10)), Sec("C", "1", Lecture, At(2, 9, 10))]));
        Assert.Equal("A 1 conflicts with B 1",
            Line([Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(0, 9, 10))]));
    }

    [Fact]
    public void OneSectionCase_CountsTheOtherConflicts()
    {
        var sections = new[]
        {
            Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(0, 9, 10)), Sec("C", "1", Lecture, At(0, 9, 10)),
        };
        Assert.Equal("A 1 conflicts with B 1 · 2 more conflicts", Line(sections));
    }

    [Fact]
    public void TooLarge_SaysSoAndNothingElse()
    {
        var sections = Enumerable.Range(0, AccessAnalyzer.MaxCourses + 1)
            .Select(i => Sec("C" + i.ToString("D2"), "1", Lecture, At(i % 5, 9, 10))).ToList();
        Assert.Equal("Too many courses to check (more than 30)", Line(sections));
    }

    [Fact]
    public void NotCountedAndWithoutTimes_AreCountedAfterTheSlates()
    {
        var inputs = Four();
        var group = new ComponentGroup("A", null, [Sec("A", "1", Lecture), Sec("A", "2", Lecture)], new HashSet<string>());
        var a = new Analysis
        {
            Health = AccessHealth.Attention, Groups = [group, group], SlateCount = 2, OneCampusRule = true,
            NotCountedSections = [Sec("A", "9", Lecture)],
            SectionsWithoutTimes = [Sec("A", "1", Lecture), Sec("B", "1", Lecture)],
            ConflictCount = 1,
        };
        Assert.Equal("2 slates · 1 section not counted · 2 sections without times · 1 conflict", Line(inputs, a));
    }
}
