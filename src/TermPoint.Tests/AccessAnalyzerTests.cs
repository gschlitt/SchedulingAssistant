using TermPoint.Models;
using TermPoint.Services.Access;
using Xunit;
using static TermPoint.Tests.AccessTestKit;

namespace TermPoint.Tests;

/// <summary>
/// <see cref="AccessAnalyzer"/> on hand-built sections: grouping (rules 7-10, 8a), slates, roles, pins, subsets,
/// the cap, best partial, self-fail, the campus rule (6d, 6e), the single-section case (6c), proposals and the
/// watch overload (D28, 8g).
/// </summary>
public sealed class AccessAnalyzerTests
{
    private static Analysis Run(IReadOnlyList<Section> sections, params Course[] courses) =>
        AnalyzeAll(Simple(sections, courses));

    // ── Rules 7-10: grouping ─────────────────────────────────────────────────

    [Fact]
    public void Rule7_OneComponent_EverySectionCountsWhateverItsType()
    {
        var a = Run([Sec("A", "1", Lecture, At(0, 9, 10)), Sec("A", "2", Online), Sec("A", "3", null, At(1, 9, 10))]);
        var group = Assert.Single(a.Groups);
        Assert.Equal(3, group.Candidates.Count);
        Assert.Empty(a.NotCountedSections);
        Assert.Equal(3, a.SlateCount);
    }

    [Fact]
    public void Rule7_NoComponents_EverySectionCounts()
    {
        var a = Run([Sec("A", "1", Online), Sec("A", "2", Lecture)], Crs("A"));
        Assert.Equal(2, Assert.Single(a.Groups).Candidates.Count);
    }

    [Fact]
    public void Rule8_TwoComponents_SectionsWithNoTypeOrANonComponentTypeAreNotCounted()
    {
        var a = Run([Sec("A", "1", Lecture), Sec("A", "L1", Lab), Sec("A", "X", null), Sec("A", "O", Online)],
            Crs("A", Lecture, Lab));
        Assert.Equal(2, a.Groups.Count);
        Assert.Equal(["A O", "A X"], Ids(a.NotCountedSections).Order().ToArray());
        Assert.Equal(AccessHealth.Attention, a.Health);
    }

    [Fact]
    public void Rule8_TwoComponents_AGroupHoldsOnlyItsOwnType()
    {
        var a = Run([Sec("A", "1", Lecture), Sec("A", "2", Lecture), Sec("A", "L1", Lab)], Crs("A", Lecture, Lab));
        Assert.Equal([2, 1], a.Groups.Select(g => g.Candidates.Count).ToArray());
        Assert.Equal([Lecture, Lab], a.Groups.Select(g => g.ComponentTypeId).ToArray());
        Assert.Equal(2, a.SlateCount);
    }

    [Fact]
    public void Rule8a_SectionOfAnUnlistedComponentType_IsAMismatch_OnlyOnceSetUp()
    {
        var sections = new[] { Sec("A", "1", Lecture), Sec("A", "L1", Lab) };
        var setUp = AnalyzeAll(Inputs(sections, [Crs("A", Lecture)]));
        var mismatch = Assert.Single(setUp.Mismatches);
        Assert.Equal(Lab, mismatch.SectionTypeId);
        Assert.Equal(["A L1"], Ids(mismatch.Sections));
        // rule 7 counts the lab as an alternative to the lecture, which is why 8a names it
        Assert.Equal(2, Assert.Single(setUp.Groups).Candidates.Count);

        var beforeSetup = AnalyzeAll(Inputs(sections, [Crs("A", Lecture)], setUp: false));
        Assert.Empty(beforeSetup.Mismatches);
    }

    [Fact]
    public void Rule8a_MismatchesLookAtTheCoursesWholeSemester_NotJustCoveredSections()
    {
        var lecture = Sec("A", "1", Lecture, tags: ["watched"]);
        var lab = Sec("A", "L1", Lab);   // untagged: not covered
        var inputs = Inputs([lecture, lab], [Crs("A", Lecture)]);
        var a = AccessAnalyzer.Analyze(inputs, new AccessWatch("watched", false));
        Assert.Single(a.Mismatches);
        Assert.Equal(["A L1"], Ids(a.Mismatches[0].Sections));
    }

    [Fact]
    public void Rule10_ComponentWithNoSections_IsMissing_AndTheCourseIsLeftOut()
    {
        var a = Run([Sec("A", "1", Lecture), Sec("B", "1", Lecture), Sec("B", "L1", Lab)], Crs("A", Lecture, Lab), Crs("B", Lecture, Lab));
        var missing = Assert.Single(a.MissingComponents);
        Assert.Equal(("A", Lab, false), (missing.CourseId, missing.ComponentTypeId, missing.SectionsExistInView));
        Assert.DoesNotContain(a.Groups, g => g.CourseId == "A");
        Assert.Equal(2, a.Groups.Count);   // B's two components
        Assert.Equal(AccessHealth.Attention, a.Health);
        Assert.True(a.HasSlate);
    }

    [Fact]
    public void Rule10_MissingComponent_SaysWhenTheViewHasSectionsTheSetDoesNotCover()
    {
        var inputs = Inputs([Sec("A", "1", Lecture, tags: ["w"]), Sec("A", "L1", Lab)], [Crs("A", Lecture, Lab)]);
        var a = AccessAnalyzer.Analyze(inputs, new AccessWatch("w", false));
        Assert.True(Assert.Single(a.MissingComponents).SectionsExistInView);
    }

    [Fact]
    public void ACourseWithNoSections_IsNotOffered()
    {
        var a = Run([Sec("A", "1", Lecture)], Crs("Z", Lecture));
        Assert.Equal(["A"], a.OfferedCourseIds);
    }

    [Fact]
    public void NoSections_NothingToTake_NoSlateAndOk()
    {
        var a = AnalyzeAll(Inputs([], []));
        Assert.True(a.HasNoGroups);
        Assert.Equal(0, a.SlateCount);
        Assert.False(a.HasSlate);
        Assert.Equal(AccessHealth.Ok, a.Health);
        Assert.Null(a.BestPartial);
    }

    // ── Rules 16-23: slates and roles ────────────────────────────────────────

    [Fact]
    public void Slates_AreCountedOneSectionPerComponent_WithNoConflicts()
    {
        var a = Run([
            Sec("A", "1", Lecture, At(0, 9, 10)), Sec("A", "2", Lecture, At(1, 9, 10)),
            Sec("B", "1", Lecture, At(0, 9, 10)), Sec("B", "2", Lecture, At(2, 9, 10))]);
        Assert.Equal(3, a.SlateCount);   // (1,2), (2,1), (2,2); not (1,1)
        Assert.Equal(1, a.ConflictCount);
    }

    [Fact]
    public void Roles_OnlyEssentialByTimingStrandedUsable()
    {
        var a = Run([
            Sec("A", "1", Lecture, At(0, 9, 10)),                                  // the only section of A
            Sec("B", "1", Lecture, At(0, 9, 10)), Sec("B", "2", Lecture, At(1, 9, 10)),   // b1 conflicts with a1
            Sec("C", "1", Lecture, At(3, 9, 10)), Sec("C", "2", Lecture, At(4, 9, 10))]);
        Assert.Equal(SectionRole.OnlySection, a.Roles["A 1"]);
        Assert.Equal(SectionRole.Stranded, a.Roles["B 1"]);
        Assert.Equal(SectionRole.EssentialByTiming, a.Roles["B 2"]);
        Assert.Equal(SectionRole.Usable, a.Roles["C 1"]);
        Assert.Equal(SectionRole.Usable, a.Roles["C 2"]);
        Assert.Equal(["A 1"], Ids(a.OnlySections));
        Assert.Equal(["B 2"], Ids(a.EssentialByTimingSections));
        Assert.Equal(["B 1"], Ids(a.StrandedSections));
        Assert.Equal(AccessHealth.Fragile, a.Health);
    }

    [Fact]
    public void StrandedReason_NamesTheAlwaysUsedSectionItConflictsWith()
    {
        var a = Run([Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(0, 9, 10)), Sec("B", "2", Lecture, At(1, 9, 10))]);
        var reason = a.StrandedReasonFor(a.StrandedSections.Single());
        Assert.Equal(StrandedKind.ConflictsWithAlwaysUsed, reason.Kind);
        Assert.Equal("A 1", reason.Other!.Id);
    }

    [Fact]
    public void StrandedReason_WhenNoSingleSectionIsToBlame_IsNoCombination()
    {
        // x conflicts with c1 and d2; c2 conflicts with d1; so x is stranded though no always-used section blocks it
        var a = Run(ThreeCourseChain());
        var x = a.StrandedSections.Single();
        Assert.Equal("B x", x.Id);
        Assert.Equal(StrandedKind.NoCombination, a.StrandedReasonFor(x).Kind);
    }

    [Fact]
    public void WhenThereIsNoSlate_NoSectionHasARole()
    {
        var a = Run([Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(0, 9, 10))]);
        Assert.False(a.HasSlate);
        Assert.All(a.Roles.Values, r => Assert.Equal(SectionRole.None, r));
        Assert.Empty(a.StrandedSections);
    }

    [Fact]
    public void SectionsWithoutTimes_FitAnythingAndAreReported()
    {
        var a = Run([Sec("A", "1", Lecture, At(0, 9, 10)), Sec("A", "2", Online)]);
        Assert.Equal(2, a.SlateCount);
        Assert.Equal(["A 2"], Ids(a.SectionsWithoutTimes));
    }

    // ── Rule 19: the cap ─────────────────────────────────────────────────────

    [Fact]
    public void Cap_CountStopsAtTheCap_AndSaysSo()
    {
        var a = AnalyzeAll(Simple(ManyFreeSections(courses: 4, perCourse: 10)), cap: 50);
        Assert.Equal(50, a.SlateCount);
        Assert.True(a.SlateCountCapped);
    }

    [Fact]
    public void Cap_NotReached_IsExactAndNotCapped()
    {
        var a = AnalyzeAll(Simple(ManyFreeSections(courses: 2, perCourse: 3)), cap: 50);
        Assert.Equal(9, a.SlateCount);
        Assert.False(a.SlateCountCapped);
    }

    [Fact]
    public void Cap_RolesStayRight_UnusedSectionsAreCompletedByExistenceSearches()
    {
        // A's only section conflicts with S2 of B, so S2 is stranded; the capped count never reaches B's later sections
        var sections = ManyFreeSections(courses: 3, perCourse: 10);
        sections.Add(Sec("A0", "only", Lecture, At(0, 9, 10)));
        sections.Add(Sec("B0", "s2", Lecture, At(0, 9, 10)));
        sections.Add(Sec("B0", "s1", Lecture, At(1, 9, 10)));
        var a = AnalyzeAll(Simple(sections), cap: 5);

        Assert.True(a.SlateCountCapped);
        Assert.Equal(SectionRole.Stranded, a.Roles["B0 s2"]);
        Assert.Equal(SectionRole.OnlySection, a.Roles["A0 only"]);
        // every free section is in some slate, including ones the capped search never reached
        foreach (var s in sections.Where(s => s.CourseId!.StartsWith("F")))
            Assert.Equal(SectionRole.Usable, a.Roles[s.Id]);
    }

    [Fact]
    public void Cap_BelowOne_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AnalyzeAll(Simple([Sec("A", "1", Lecture)]), cap: 0));

    // ── Rule 18: pins; rule 17: subsets ──────────────────────────────────────

    [Fact]
    public void Pin_NarrowsItsGroupToThePinnedSection()
    {
        var inputs = Simple([Sec("A", "1", Lecture), Sec("A", "2", Lecture), Sec("B", "1", Lecture)]);
        var a = AnalyzeAll(inputs, pins: new HashSet<string> { "A 2" });
        var group = a.Groups.Single(g => g.CourseId == "A");
        Assert.True(group.Pinned);
        Assert.Equal(["A 2"], Ids(group.Candidates));
        Assert.Equal(2, group.All.Count);
        Assert.Equal(1, a.SlateCount);
        Assert.Empty(a.OnlySections.Where(s => s.Id == "A 2"));   // a pinned group's section isn't "the only section"
    }

    [Fact]
    public void Pins_ThatConflictWithEachOther_ArePinBlocksNotABestPartial()
    {
        var inputs = Simple([
            Sec("A", "1", Lecture, At(0, 9, 10)), Sec("A", "2", Lecture, At(1, 9, 10)),
            Sec("B", "1", Lecture, At(0, 9, 10)), Sec("B", "2", Lecture, At(2, 9, 10))]);
        var a = AnalyzeAll(inputs, pins: new HashSet<string> { "A 1", "B 1" });
        Assert.False(a.HasSlate);
        Assert.Null(a.BestPartial);
        Assert.Empty(a.SelfFailCourses);
        Assert.NotEmpty(a.PinBlocks);
        Assert.All(a.PinBlocks, b => Assert.NotNull(b.Pin));
    }

    [Fact]
    public void PinBlock_NamesThePinThatCannotFitWhileTheOthersAreKept()
    {
        // B's only section b1 conflicts with a1; pinning a1 (alone) is fine, pinning a1 and b1 together is not.
        // With a1 kept, b1 is stranded: it is the pin to blame.
        var inputs = Simple([
            Sec("A", "1", Lecture, At(0, 9, 10)), Sec("A", "2", Lecture, At(1, 9, 10)),
            Sec("B", "1", Lecture, At(0, 9, 10))]);
        var a = AnalyzeAll(inputs, pins: new HashSet<string> { "A 1" });
        Assert.False(a.HasSlate);   // b1 is B's only section and conflicts with the pinned a1
        var block = Assert.Single(a.PinBlocks);
        Assert.Equal("A 1", block.Pin!.Id);
    }

    [Fact]
    public void Subset_AnalysesOnlyTheChosenCourses()
    {
        var inputs = Simple([Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(0, 9, 10)), Sec("C", "1", Lecture, At(1, 9, 10))]);
        var all = AnalyzeAll(inputs);
        var subset = AnalyzeAll(inputs, subset: new HashSet<string> { "A", "C" });
        Assert.False(all.HasSlate);
        Assert.True(subset.HasSlate);
        Assert.True(subset.IsSubset);
        Assert.Equal(["A", "B", "C"], subset.OfferedCourseIds);
        Assert.Equal(["A", "C"], subset.ChosenCourseIds);
    }

    // ── Rule 23: best partial; self-fail ─────────────────────────────────────

    [Fact]
    public void BestPartial_WithTies_ListsEveryLeftOutAlternative()
    {
        // B and C conflict with each other; A fits with either
        var a = Run([Sec("A", "1", Lecture, At(0, 8, 9)), Sec("B", "1", Lecture, At(0, 9, 10)), Sec("C", "1", Lecture, At(0, 9, 10))]);
        Assert.False(a.HasSlate);
        Assert.Empty(a.SelfFailCourses);
        var best = a.BestPartial!;
        Assert.Equal((2, 3), (best.Covers, best.Of));
        Assert.Equal([["C"], ["B"]], best.Alternatives.Select(x => x.ToArray()).ToArray());
        Assert.Equal(["C", "B"], best.CantFit);
        Assert.Equal(AccessHealth.Attention, a.Health);
    }

    [Fact]
    public void BestPartial_OneWay_NamesTheLeftOutCourses()
    {
        // C conflicts with both A and B; A and B fit together
        var a = Run([Sec("A", "1", Lecture, At(0, 8, 9)), Sec("B", "1", Lecture, At(1, 8, 9)),
            Sec("C", "1", Lecture, At([0, 1], 8, 9))]);
        var best = a.BestPartial!;
        Assert.Equal(2, best.Covers);
        Assert.Equal([["C"]], best.Alternatives.Select(x => x.ToArray()).ToArray());
    }

    [Fact]
    public void SelfFail_ACourseWhoseOnlyLectureLandsOnAllItsLabs_FailsAlone()
    {
        var a = Run([
            Sec("X", "lec", Lecture, At(0, 9, 11)), Sec("X", "L1", Lab, At(0, 10, 12)), Sec("X", "L2", Lab, At(0, 9, 10)),
            Sec("Y", "1", Lecture, At(3, 9, 10))], Crs("X", Lecture, Lab));
        Assert.False(a.HasSlate);
        var fail = Assert.Single(a.SelfFailCourses);
        Assert.Equal("X", fail.CourseId);
        Assert.Equal(SelfFailKind.TimeConflict, fail.Reason.Kind);
        Assert.Equal(["X lec"], Ids(fail.Reason.OnlyGroup!.Candidates));
        Assert.Equal(Lab, fail.Reason.OtherGroup!.ComponentTypeId);
        Assert.Equal(2, fail.Reason.OtherGroup.Candidates.Count);
        // the rest still fits: the best partial leaves X out first and covers the other course
        Assert.Equal((1, 2), (a.BestPartial!.Covers, a.BestPartial.Of));
        Assert.Empty(a.BestPartial.Alternatives);
        Assert.Equal(["X"], a.BestPartial.CantFit);
        Assert.Null(a.CampusReason);
    }

    [Fact]
    public void SelfFail_WhenNoSingleSectionIsToBlame_IsNoFit()
    {
        // two lectures and two labs, and every lecture conflicts with every lab (each on a different day)
        var a = Run([
            Sec("X", "1", Lecture, At([0, 1], 9, 10)), Sec("X", "2", Lecture, At([2, 3], 9, 10)),
            Sec("X", "L1", Lab, At([0, 2], 9, 10)), Sec("X", "L2", Lab, At([1, 3], 9, 10))], Crs("X", Lecture, Lab));
        Assert.False(a.HasSlate);
        Assert.Equal(SelfFailKind.NoFit, Assert.Single(a.SelfFailCourses).Reason.Kind);
    }

    // ── Rules 15, 6d, 6e: the campus rule ────────────────────────────────────

    private static List<Section> TwoCampusesOneSlateMixes() =>
    [
        Sec("A", "1", Lecture, At(0, 9, 10), "abb"),
        Sec("B", "1", Lecture, At(1, 9, 10), "chil"),
    ];

    [Fact]
    public void CampusRuleOn_OnlySectionsAtDifferentCampuses_NoSlate_AndTheCampusReasonNamesThem()
    {
        var a = Run(TwoCampusesOneSlateMixes());
        Assert.False(a.HasSlate);
        Assert.True(a.OneCampusRule);
        Assert.Empty(a.SelfFailCourses);
        var reason = a.CampusReason!;
        Assert.Equal(CampusReasonKind.OnlySectionsOnDifferentCampuses, reason.Kind);
        Assert.Equal(2, new[] { reason.LessCommonCampusId, reason.MoreCommonCampusId }.Distinct().Count());
    }

    [Fact]
    public void CampusRuleOff_MixedSlatesAreAllowed_AndCountedAsMixed()
    {
        var a = AnalyzeAll(Simple(TwoCampusesOneSlateMixes()), multiCampus: true);
        Assert.Equal(1, a.SlateCount);
        Assert.Equal(0, a.OneCampusSlateCount);
        Assert.Equal(1, a.MixedSlateCount);
        Assert.NotNull(a.MixedCampusReason);
        Assert.Equal(AccessHealth.Fragile, a.Health);
        Assert.True(a.SpansCampuses);
    }

    [Fact]
    public void CampusRuleOff_SomeSlatesKeepToOneCampus_NoMixedReason()
    {
        var sections = new List<Section>
        {
            Sec("A", "1", Lecture, At(0, 9, 10), "abb"),
            Sec("B", "1", Lecture, At(1, 9, 10), "abb"), Sec("B", "2", Lecture, At(2, 9, 10), "chil"),
        };
        var a = AnalyzeAll(Simple(sections), multiCampus: true);
        Assert.Equal((2, 1, 1), (a.SlateCount, a.OneCampusSlateCount, a.MixedSlateCount));
        Assert.Null(a.MixedCampusReason);
        Assert.Equal(AccessHealth.Ok, a.Health);

        var off = AnalyzeAll(Simple(sections));
        Assert.Equal(1, off.SlateCount);
        Assert.Null(off.OneCampusSlateCount);
    }

    [Fact]
    public void CampusRule_OnlineAndNoCampusSectionsFitAnyCampus()
    {
        var sections = new List<Section>
        {
            Sec("A", "1", Lecture, At(0, 9, 10), "abb"),
            Sec("B", "1", Lecture, At(1, 9, 10), "chil"),
            Sec("C", "1", Lecture, At(2, 9, 10)),   // no campus
        };
        sections[1].Schedule[0].RoomTypeId = SectionDaySchedule.RemoteRoomTypeId;   // B's only meeting is remote
        Assert.True(AnalyzeAll(Simple(sections)).HasSlate);
    }

    [Fact]
    public void MixedCount_IsNull_WhenTheCountWasCapped()
    {
        var sections = new List<Section>();
        for (int i = 0; i < 10; i++) sections.Add(Sec("A", "a" + i, Lecture, At(0, 9, 10), i % 2 == 0 ? "abb" : "chil"));
        for (int i = 0; i < 10; i++) sections.Add(Sec("B", "b" + i, Lecture, At(1, 9, 10), "abb"));
        var a = AnalyzeAll(Simple(sections), multiCampus: true, cap: 20);
        Assert.True(a.SlateCountCapped);
        Assert.Null(a.MixedSlateCount);
    }

    [Fact]
    public void CampusReason_WhenNoSingleSectionComponentsExplainIt_IsTheGeneralKind()
    {
        // each course has two sections, one per campus, and the same-campus pairs conflict in time: only mixed slates fit
        var sections = new List<Section>
        {
            Sec("A", "1", Lecture, At(0, 9, 10), "abb"), Sec("A", "2", Lecture, At(1, 9, 10), "chil"),
            Sec("B", "1", Lecture, At(0, 9, 10), "abb"), Sec("B", "2", Lecture, At(1, 9, 10), "chil"),
        };
        var a = Run(sections);
        Assert.False(a.HasSlate);
        Assert.Equal(CampusReasonKind.NoSlateKeepsToOneCampus, a.CampusReason!.Kind);
        Assert.True(AnalyzeAll(Simple(sections), multiCampus: true).HasSlate);
    }

    // ── Round 6c: the single-section case ────────────────────────────────────

    [Fact]
    public void SingleSectionCase_OneSectionPerComponent_ReportsConflictingPairs()
    {
        var a = Run([Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(0, 9, 10)), Sec("C", "1", Lecture, At(1, 9, 10))]);
        Assert.True(a.IsSingleSectionCase);
        var pair = Assert.Single(a.SingleSectionConflicts);
        Assert.Equal(("A 1", "B 1"), (pair.A.Id, pair.B.Id));
        Assert.False(a.HasSlate);
    }

    [Fact]
    public void SingleSectionCase_WhenTheyFit_HasNoConflictsAndOneSlate()
    {
        var a = Run([Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(1, 9, 10))]);
        Assert.True(a.IsSingleSectionCase);
        Assert.Empty(a.SingleSectionConflicts);
        Assert.Equal(1, a.SlateCount);
    }

    [Fact]
    public void SingleSectionCase_IsFalseWhenAnyComponentHasAlternatives()
    {
        var a = Run([Sec("A", "1", Lecture), Sec("A", "2", Lecture)]);
        Assert.False(a.IsSingleSectionCase);
    }

    [Fact]
    public void SingleSectionCase_UsesTheSectionsBeforePins()
    {
        var a = AnalyzeAll(Simple([Sec("A", "1", Lecture), Sec("A", "2", Lecture)]), pins: new HashSet<string> { "A 1" });
        Assert.False(a.IsSingleSectionCase);
    }

    // ── Rules 28-30: proposals ───────────────────────────────────────────────

    [Fact]
    public void Proposal_MovingASection_ChangesTheAnswer_AndTheEmptyProposalIsTheSavedAnswer()
    {
        var saved = Simple([Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(0, 9, 10))]);
        Assert.False(AnalyzeAll(saved).HasSlate);
        Assert.False(AnalyzeAll(saved.With(Proposal.Empty)).HasSlate);

        var moved = saved.With(new Proposal([Sec("B", "1", Lecture, At(1, 9, 10))], []));
        Assert.True(AnalyzeAll(moved).HasSlate);
        Assert.False(AnalyzeAll(saved).HasSlate);   // saved state unchanged
    }

    [Fact]
    public void Proposal_RemovingTheOnlyLab_MakesTheComponentMissing()
    {
        var saved = Inputs([Sec("A", "1", Lecture), Sec("A", "L1", Lab)], [Crs("A", Lecture, Lab)]);
        Assert.Empty(AnalyzeAll(saved).MissingComponents);
        var view = saved.With(new Proposal([], ["A L1"]));
        Assert.Equal(Lab, Assert.Single(AnalyzeAll(view).MissingComponents).ComponentTypeId);
    }

    [Fact]
    public void Proposal_ChangingASectionsType_MovesItBetweenComponents()
    {
        var saved = Inputs([Sec("A", "1", Lecture), Sec("A", "L1", Lecture)], [Crs("A", Lecture, Lab)]);
        Assert.Single(AnalyzeAll(saved).MissingComponents);   // both are lectures: no lab
        var view = saved.With(new Proposal([Sec("A", "L1", Lab)], []));
        var a = AnalyzeAll(view);
        Assert.Empty(a.MissingComponents);
        Assert.Equal(2, a.Groups.Count);
    }

    // ── The watch overload (D28, 8g) ─────────────────────────────────────────

    [Fact]
    public void Watch_CoversExactlyTheSectionsCarryingTheTag()
    {
        var inputs = Simple([
            Sec("A", "1", Lecture, tags: ["w"]), Sec("A", "2", Lecture), Sec("B", "1", Lecture, tags: ["w", "other"]), Sec("C", "1", Lecture, tags: ["other"])]);
        var a = AccessAnalyzer.Analyze(inputs, new AccessWatch("w", false));
        Assert.Equal(["A", "B"], a.OfferedCourseIds);
        Assert.Equal(1, a.SlateCount);   // A 1 with B 1
    }

    [Fact]
    public void Watch_FlagsASemesterSectionOfAWatchedCourseThatLacksTheTag()
    {
        var tagged = Simple([Sec("A", "1", Lecture, tags: ["w"]), Sec("A", "2", Lecture, tags: ["w"])]);
        Assert.False(AccessAnalyzer.Analyze(tagged, new AccessWatch("w", false)).HasUntaggedSections);
        var partly = Simple([Sec("A", "1", Lecture, tags: ["w"]), Sec("A", "2", Lecture), Sec("Z", "1", Lecture)]);
        Assert.True(AccessAnalyzer.Analyze(partly, new AccessWatch("w", false)).HasUntaggedSections);
        // the plain overload never sets it
        Assert.False(AnalyzeAll(partly).HasUntaggedSections);
    }

    [Fact]
    public void Watch_FromTag_ReadsTheMultiCampusBox()
    {
        var tag = new SchedulingEnvironmentValue { Id = "t", Name = "Enviro Y2", IsAccessWatch = true, WatchMultiCampus = true };
        var watch = AccessWatch.FromTag(tag);
        Assert.Equal(("t", true), (watch.TagId, watch.MultiCampus));
        var inputs = Simple(TwoCampusesOneSlateMixes().Select(s => { s.TagIds.Add("t"); return s; }).ToList());
        Assert.True(AccessAnalyzer.Analyze(inputs, watch).HasSlate);   // Multi-campus on: the mixed slate counts
        Assert.False(AccessAnalyzer.Analyze(inputs, watch with { MultiCampus = false }).HasSlate);
    }

    // ── Slate listing (Q34) ──────────────────────────────────────────────────

    [Fact]
    public void ListSlates_InStableOrder_UpToTheCap()
    {
        var inputs = Simple([
            Sec("A", "2", Lecture), Sec("A", "1", Lecture), Sec("B", "2", Lecture), Sec("B", "1", Lecture)]);
        var a = AnalyzeAll(inputs);
        var search = new SlateSearch(a.Groups, true);
        var slates = search.List(10);
        Assert.Equal(4, slates.Count);
        Assert.Equal(["A 1", "B 1"], Ids(slates[0]));
        Assert.Equal(["A 1", "B 2"], Ids(slates[1]));
        Assert.Equal(["A 2", "B 1"], Ids(slates[2]));
        Assert.Equal(2, search.List(2).Count);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Courses B, C, D (one component each): x (in B) conflicts with c1 and d2, and c2 conflicts with d1, so x is
    /// stranded although no single always-used section blocks it. Section ids are "course code".
    /// </summary>
    internal static List<Section> ThreeCourseChain() =>
    [
        Sec("B", "x", Lecture, At(0, 9, 10).Concat(At(1, 9, 10))),
        Sec("B", "y", Lecture, At(4, 9, 10)),
        Sec("C", "c1", Lecture, At(0, 9, 10)),
        Sec("C", "c2", Lecture, At(2, 9, 10)),
        Sec("D", "d1", Lecture, At(2, 9, 10)),
        Sec("D", "d2", Lecture, At(1, 9, 10)),
    ];

    /// <summary>Courses F0..F(n-1), each with sections that have no meetings (so they never conflict).</summary>
    private static List<Section> ManyFreeSections(int courses, int perCourse)
    {
        var list = new List<Section>();
        for (int c = 0; c < courses; c++)
            for (int s = 0; s < perCourse; s++)
                list.Add(Sec("F" + c, "s" + s.ToString("D2"), Lecture));
        return list;
    }
}
