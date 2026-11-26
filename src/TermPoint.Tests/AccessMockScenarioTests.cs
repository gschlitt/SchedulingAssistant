using TermPoint.Models;
using TermPoint.Services.Access;
using Xunit;
using static TermPoint.Tests.AccessMockSemester;
using static TermPoint.Tests.AccessTestKit;

namespace TermPoint.Tests;

/// <summary>
/// The r9 wireframe's mock semester (<see cref="AccessMockSemester"/>) run through the engine: the scenarios the
/// design was agreed on, a brute-force cross-check of every slate count in every variant, and Check all courses.
/// </summary>
public sealed class AccessMockScenarioTests
{
    private static Analysis Watch(int variant, string tag, bool multiCampus = false, Proposal? proposal = null) =>
        AccessAnalyzer.Analyze(AccessMockSemester.Inputs(variant, proposal), new AccessWatch(tag, multiCampus));

    // ── Cross-check ──────────────────────────────────────────────────────────

    /// <summary>
    /// For every variant, watch and Multi-campus setting, the engine's slate count equals a plain enumeration of
    /// every combination, checked with the public conflict and campus tests.
    /// </summary>
    [Fact]
    public void EverySlateCount_MatchesABruteForceEnumeration()
    {
        foreach (int variant in Enumerable.Range(0, 9))
            foreach (var tag in new[] { EnviroY2, ChemY2, BiomedY2 })
                foreach (bool multi in new[] { false, true })
                {
                    var a = Watch(variant, tag, multi);
                    int expected = a.HasNoGroups ? 0 : BruteForceCount(a.Groups, !multi);
                    Assert.True(expected < AccessAnalyzer.DefaultCap, $"variant {variant} {tag}: too big to cross-check");
                    Assert.True(expected == a.SlateCount, $"variant {variant}, {tag}, multi={multi}: engine {a.SlateCount}, brute force {expected}");
                }
    }

    [Fact]
    public void ListedSlates_AreAllValid_AndMatchTheCount()
    {
        var a = Watch(0, ChemY2);
        var slates = new SlateSearch(a.Groups, true).List(AccessAnalyzer.DefaultCap);
        Assert.Equal(a.SlateCount, slates.Count);
        foreach (var slate in slates)
            for (int i = 0; i < slate.Count; i++)
                for (int j = i + 1; j < slate.Count; j++)
                    Assert.False(SectionOverlap.Conflicts(slate[i], slate[j]));
    }

    // ── Scenarios ────────────────────────────────────────────────────────────

    [Fact]
    public void BiomedY2_AsPlanned_BMEG230X01HasNoTypeSoItIsNotCounted()
    {
        var a = Watch(0, BiomedY2);
        Assert.Equal(["BMEG 230 X01"], Ids(a.NotCountedSections));
        Assert.Empty(a.MissingComponents);
        Assert.Equal(AccessHealth.Attention, a.Health);   // sections not counted
        Assert.True(a.HasSlate);
        Assert.Equal(["BMEG 230", "CHBE 201", "ELEC 203"], a.OfferedCourseIds);
    }

    [Fact]
    public void BiomedY2_Variant5_TheOnlyBMEGLectureLandsOnAllLabs_SelfFail()
    {
        var a = Watch(5, BiomedY2);
        Assert.False(a.HasSlate);
        var fail = Assert.Single(a.SelfFailCourses);
        Assert.Equal("BMEG 230", fail.CourseId);
        Assert.Equal(SelfFailKind.TimeConflict, fail.Reason.Kind);
        Assert.Equal(["BMEG 230 101"], Ids(fail.Reason.OnlyGroup!.Candidates));
        Assert.Equal(Lab, fail.Reason.OtherGroup!.ComponentTypeId);
        Assert.Equal(3, fail.Reason.OtherGroup.Candidates.Count);
        Assert.Equal((2, 3), (a.BestPartial!.Covers, a.BestPartial.Of));
        Assert.Equal(["BMEG 230"], a.BestPartial.CantFit);
        Assert.Null(a.CampusReason);
    }

    [Fact]
    public void BiomedY2_Variant5_Marks_MovingTheLectureIsTheOneMove()
    {
        // the lecture lands on every lab and on both ELEC 203 sections: moving it alone brings a full slate back
        var marks = ConflictMarks.For(AccessMockSemester.Inputs(5), new AccessWatch(BiomedY2, false), false);
        Assert.NotEmpty(marks);
        Assert.All(marks, m => Assert.Equal(ConflictMarkKind.Move, m.Kind));
        var option = Assert.Single(marks[0].Problem!.Options);
        Assert.Equal("BMEG 230 101", option.Section.Id);
        var partners = Ids(option.ConflictsWith);
        foreach (var id in new[] { "BMEG 230 L1A", "BMEG 230 L1B", "BMEG 230 L1C", "ELEC 203 101", "ELEC 203 102" })
            Assert.Contains(id, partners);
        // T1B lets students get around T1A, so the lecture's conflict with T1A is not part of the move
        Assert.DoesNotContain("BMEG 230 T1A", partners);
        Assert.DoesNotContain("CHBE 201 101", partners);
    }

    [Fact]
    public void EnviroY2_Variant6_CHEM213AtChilliwack_CampusReasonNamesTheOnlySections()
    {
        var oneCampus = Watch(6, EnviroY2);
        Assert.False(oneCampus.HasSlate);
        Assert.Empty(oneCampus.SelfFailCourses);
        var reason = oneCampus.CampusReason!;
        Assert.Equal(CampusReasonKind.OnlySectionsOnDifferentCampuses, reason.Kind);
        Assert.Equal(Chilliwack, reason.LessCommonCampusId);
        Assert.Equal(["CHEM 213 A"], Ids(reason.LessCommonOnlySections));
        Assert.Equal(Abbotsford, reason.MoreCommonCampusId);
        Assert.Equal(["BIOL 201 A"], Ids(reason.MoreCommonOnlySections));
        Assert.True(oneCampus.SpansCampuses);

        // with Multi-campus on the slates exist, and every one mixes campuses
        var multi = Watch(6, EnviroY2, multiCampus: true);
        Assert.True(multi.HasSlate);
        Assert.Equal(0, multi.OneCampusSlateCount);
        Assert.Equal(multi.SlateCount, multi.MixedSlateCount);
        Assert.NotNull(multi.MixedCampusReason);
        Assert.Equal(AccessHealth.Fragile, multi.Health);
    }

    [Fact]
    public void EnviroY2_AsPlanned_BIOL201L3AtChilliwackIsStrandedUnderTheOneCampusRule()
    {
        var a = Watch(0, EnviroY2);
        Assert.True(a.HasSlate);
        Assert.Contains("BIOL 201 L3", Ids(a.StrandedSections));
        // with Multi-campus on it is usable
        Assert.Equal(SectionRole.Usable, Watch(0, EnviroY2, true).Roles["BIOL 201 L3"]);
    }

    [Fact]
    public void ChemY2_ASectionOfAWatchedCourseWithoutTheTag_IsFlagged_AndNotCovered()
    {
        var inputs = AccessMockSemester.Inputs();
        // 8g: a section of a watched course that lacks the tag
        inputs.SectionsByCourse["MATH 111"].Single(s => s.SectionCode == "B").TagIds.Remove(ChemY2);
        var a = AccessAnalyzer.Analyze(inputs, new AccessWatch(ChemY2, false));
        Assert.True(a.HasUntaggedSections);
        Assert.DoesNotContain("MATH 111 B", Ids(a.Groups.SelectMany(g => g.All)));
        Assert.False(AccessAnalyzer.Analyze(AccessMockSemester.Inputs(), new AccessWatch(ChemY2, false)).HasUntaggedSections);
    }

    [Fact]
    public void Variant8_BMEG230LosesItsTutorials_TheComponentIsMissing()
    {
        var a = Watch(8, BiomedY2);
        var missing = Assert.Single(a.MissingComponents);
        Assert.Equal(("BMEG 230", Discussion, false), (missing.CourseId, missing.ComponentTypeId, missing.SectionsExistInView));
        Assert.DoesNotContain(a.Groups, g => g.CourseId == "BMEG 230");
        Assert.True(a.HasSlate);   // CHBE 201 and ELEC 203 still fit
        Assert.Equal(AccessHealth.Attention, a.Health);
    }

    [Fact]
    public void Proposal_MovingBiomedLectureOntoItsLabs_LosesTheSlate()
    {
        var saved = AccessMockSemester.Inputs();
        Assert.True(AccessAnalyzer.Analyze(saved, new AccessWatch(BiomedY2, false)).HasSlate);
        var lecture = saved.Sections.Single(s => s.Id == "BMEG 230 101");
        var moved = Sec("BMEG 230", "101", Lecture, At([1, 2, 4], 14, 15.33), Abbotsford, [BiomedY2]);
        var view = saved.With(new Proposal([moved], []));
        Assert.False(AccessAnalyzer.Analyze(view, new AccessWatch(BiomedY2, false)).HasSlate);
        Assert.NotSame(lecture, moved);
        Assert.True(AccessAnalyzer.Analyze(saved, new AccessWatch(BiomedY2, false)).HasSlate);   // saved unchanged
    }

    // ── Check all courses ────────────────────────────────────────────────────

    [Fact]
    public void CheckAllCourses_AsPlanned_FindsBIOL340L1AndBIOL201L3_AndQuietFindings()
    {
        var result = CheckAllCourses.Run(AccessMockSemester.Inputs());
        Assert.Equal(14, result.OfferedCourseCount);
        Assert.Equal(4, result.MultiComponentCourseCount);   // BIOL 201, CHEM 201, BMEG 230, BIOL 340

        var stranded = result.Problems.Where(p => p.Kind == CheckProblemKind.StrandedSection).ToList();
        var l1 = stranded.Single(p => p.Section!.Id == "BIOL 340 L1");
        Assert.Equal(StrandedKind.ConflictsWithAlwaysUsed, l1.StrandedReason!.Kind);
        Assert.Equal("BIOL 340 A", l1.StrandedReason.Other!.Id);
        // a course alone follows the one-campus rule, so BIOL 201 L3 (Chilliwack) is stranded
        var l3 = stranded.Single(p => p.Section!.Id == "BIOL 201 L3");
        Assert.Equal(StrandedKind.CampusConflictsWithAlwaysUsed, l3.StrandedReason!.Kind);
        Assert.Equal(["BIOL 201 L3", "BIOL 340 L1"], stranded.Select(p => p.Section!.Id).Order().ToArray());
        Assert.DoesNotContain(result.Problems, p => p.Kind == CheckProblemKind.NoSlate);

        // BMEG 230 X01 has no section type: quiet
        var quiet = Assert.Single(result.Quiet);
        Assert.Equal((QuietFindingKind.SectionNotCounted, "BMEG 230"), (quiet.Kind, quiet.CourseId));
        Assert.Equal(["BMEG 230 X01"], Ids(quiet.Sections));
    }

    [Fact]
    public void CheckAllCourses_Variant5_BMEG230HasNoSlate_WithItsSelfFailReason()
    {
        var result = CheckAllCourses.Run(AccessMockSemester.Inputs(5));
        var problem = result.Problems.Single(p => p.Kind == CheckProblemKind.NoSlate);
        Assert.Equal("BMEG 230", problem.CourseId);
        Assert.Equal(SelfFailKind.TimeConflict, problem.SelfFail!.Kind);
        Assert.Null(problem.CampusReason);
    }

    [Fact]
    public void CheckAllCourses_Variant8_AMissingComponentIsQuiet_AndTheCourseIsNotJudged()
    {
        var result = CheckAllCourses.Run(AccessMockSemester.Inputs(8));
        Assert.Contains(result.Quiet, q => q.Kind == QuietFindingKind.MissingComponent && q.CourseId == "BMEG 230" && q.ComponentTypeId == Discussion);
        Assert.DoesNotContain(result.Problems, p => p.CourseId == "BMEG 230");
    }

    [Fact]
    public void CheckAllCourses_ReportsMismatches_ForEveryCourse_OnlyOnceSetUp()
    {
        var sections = new[] { Sec("A", "1", Lecture), Sec("A", "L1", Lab) };
        var setUp = CheckAllCourses.Run(Inputs(sections, [Crs("A", Lecture)]));
        var finding = Assert.Single(setUp.Quiet);
        Assert.Equal((QuietFindingKind.ComponentMismatch, Lab), (finding.Kind, finding.ComponentTypeId));
        Assert.Equal(["A L1"], Ids(finding.Sections));
        Assert.Equal(0, setUp.MultiComponentCourseCount);

        Assert.Empty(CheckAllCourses.Run(Inputs(sections, [Crs("A", Lecture)], setUp: false)).Quiet);
    }

    [Fact]
    public void CheckAllCourses_ACourseWithNoSlateOnItsOwn_IsAProblem_WithTheCampusSelfFail()
    {
        // lecture at one campus, its only lab at another: the one-campus rule alone leaves no slate
        var sections = new[]
        {
            Sec("A", "1", Lecture, At(0, 9, 10), "abb"), Sec("A", "L1", Lab, At(1, 9, 10), "chil"),
        };
        var result = CheckAllCourses.Run(Inputs(sections, [Crs("A", Lecture, Lab)]));
        var problem = Assert.Single(result.Problems);
        Assert.Equal(CheckProblemKind.NoSlate, problem.Kind);
        Assert.Equal(SelfFailKind.Campus, problem.SelfFail!.Kind);
    }

    [Fact]
    public void CheckAllCourses_NothingToCheck_WhenNoCourseHasTwoComponents()
    {
        var result = CheckAllCourses.Run(Inputs([Sec("A", "1", Lecture), Sec("B", "1", Lecture)], [Crs("A", Lecture), Crs("B", Lecture)]));
        Assert.Equal((2, 0), (result.OfferedCourseCount, result.MultiComponentCourseCount));
        Assert.Empty(result.Problems);
        Assert.Empty(result.Quiet);
    }
}
