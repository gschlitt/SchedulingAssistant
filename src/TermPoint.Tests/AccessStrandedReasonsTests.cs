using TermPoint.Models;
using TermPoint.Services.Access;
using TermPoint.ViewModels.Access;
using Xunit;
using static TermPoint.Tests.AccessTestKit;

namespace TermPoint.Tests;

/// <summary>
/// <see cref="StrandedReasons"/> (the narrative behind the stranded sections and move options, Greg 2026-11-27) and
/// its wording (<see cref="AccessText.StrandedClause"/>, <see cref="AccessText.WhyLines"/>,
/// <see cref="AccessText.MoveTip"/>): hand-built cases for each kind of step, the strings Greg approved, and a brute-force
/// check that nothing the narrative rules out is in any slate that counts.
/// </summary>
public sealed class AccessStrandedReasonsTests
{
    /// <summary>A forced chain: A's only section rules out B b1, which leaves B b2 as the only B, which rules out C c1.</summary>
    private static List<Section> ForcedChain() =>
    [
        Sec("A", "1", Lecture, At(0, 9, 10)),
        Sec("B", "b1", Lecture, At(0, 9, 10)), Sec("B", "b2", Lecture, At(1, 9, 10)),
        Sec("C", "c1", Lecture, At(1, 9, 10)), Sec("C", "c2", Lecture, At(2, 9, 10)),
    ];

    /// <summary>Both A sections fill Monday 9 (A's remaining domain), so B b1 (Monday 9) is out whichever A is taken.</summary>
    private static List<Section> SizeOneFootprint() =>
    [
        Sec("A", "a1", Lecture, At(0, 9, 10)), Sec("A", "a2", Lecture, At(0, 9, 10)),
        Sec("B", "b1", Lecture, At(0, 9, 10)), Sec("B", "b2", Lecture, At(1, 9, 10)),
    ];

    /// <summary>C and D together always fill Monday and Tuesday at 9; E x1 (Monday 9) is out; then E x2 rules out Z z1.</summary>
    private static List<Section> TwoGroupFootprintWithTimes() =>
    [
        Sec("C", "c1", Lecture, At(0, 9, 10)), Sec("C", "c2", Lecture, At(1, 9, 10)),
        Sec("D", "d1", Lecture, At(0, 9, 10)), Sec("D", "d2", Lecture, At(1, 9, 10)),
        Sec("E", "x1", Lecture, At(0, 9, 10)), Sec("E", "x2", Lecture, At(2, 9, 10)),
        Sec("Z", "z1", Lecture, At(2, 9, 10)), Sec("Z", "z2", Lecture, At(3, 9, 10)),
    ];

    /// <summary>The three-course chain (B x is out because every way of taking C and D conflicts with it, with no one common time), plus Z.</summary>
    private static List<Section> TwoGroupFootprintWithoutTimes() =>
        [.. AccessAnalyzerTests.ThreeCourseChain(), Sec("Z", "z1", Lecture, At(4, 9, 10)), Sec("Z", "z2", Lecture, At(3, 9, 10))];

    private static (AccessInputs Inputs, Analysis Analysis) Run(IReadOnlyList<Section> sections)
    {
        var inputs = Simple(sections);
        return (inputs, AnalyzeAll(inputs));
    }

    private static Section Sect(AccessInputs inputs, string id) => inputs.Sections.Single(s => s.Id == id);

    // ── The engine ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ForcedChain_EachStepRestsOnTheOneBefore()
    {
        var (inputs, a) = Run(ForcedChain());
        var reasons = a.Reasons;
        Assert.Equal(2, reasons.Steps.Count);

        var first = reasons.Steps[0];
        Assert.Equal(ReasonStepKind.Clash, first.Kind);
        Assert.Equal(["A 1"], Ids(first.Against));
        Assert.False(first.Narrowed);
        Assert.Equal(["B b1"], Ids(first.RuledOut));
        Assert.Empty(first.After);

        var second = reasons.Steps[1];
        Assert.Equal(["B b2"], Ids(second.Against));
        Assert.True(second.Narrowed);
        Assert.Equal(["C c1"], Ids(second.RuledOut));
        Assert.Equal([0], second.After);

        Assert.Equal([0, 1], reasons.ChainFor(Sect(inputs, "C c1")).Select(s => s.Index));
        Assert.Equal([0], reasons.ChainFor(Sect(inputs, "B b1")).Select(s => s.Index));
        Assert.Null(reasons.StepFor(Sect(inputs, "C c2")));
        Assert.Empty(reasons.ChainFor(Sect(inputs, "C c2")));
    }

    [Fact]
    public void SizeOneFootprint_IsAClashAgainstTheWholeRemainingGroup()
    {
        var (inputs, a) = Run(SizeOneFootprint());
        var step = Assert.Single(a.Reasons.Steps);
        Assert.Equal(ReasonStepKind.Clash, step.Kind);
        Assert.Equal(0, step.Group);
        Assert.Equal(["A a1", "A a2"], Ids(step.Against));
        Assert.False(step.Narrowed);
        Assert.Equal(["B b1"], Ids(step.RuledOut));
        Assert.Same(step, a.Reasons.StepFor(Sect(inputs, "B b1")));
    }

    [Fact]
    public void TwoGroupFootprint_RecordsTheTimesEveryCombinationOccupies()
    {
        var (inputs, a) = Run(TwoGroupFootprintWithTimes());
        var step = a.Reasons.Steps[0];
        Assert.Equal(ReasonStepKind.Footprint, step.Kind);
        Assert.Equal([0, 1], step.Groups);
        Assert.Equal(["E x1"], Ids(step.RuledOut));
        Assert.Equal([new CommonTime(1, 540, 600), new CommonTime(2, 540, 600)], step.CommonTimes);
        Assert.True(StrandedReasons.OverlapsCommonTimes(step, Sect(inputs, "E x1")));
        Assert.False(StrandedReasons.OverlapsCommonTimes(step, Sect(inputs, "E x2")));
    }

    [Fact]
    public void TwoGroupFootprint_WithNoCommonTime_HasNone()
    {
        var (inputs, a) = Run(AccessAnalyzerTests.ThreeCourseChain());
        var step = Assert.Single(a.Reasons.Steps);
        Assert.Equal(ReasonStepKind.Footprint, step.Kind);
        Assert.Empty(step.CommonTimes);
        Assert.Equal(["B x"], Ids(step.RuledOut));
        Assert.False(StrandedReasons.OverlapsCommonTimes(step, Sect(inputs, "B x")));
    }

    [Fact]
    public void NoNarrative_WhenNothingIsJudged_OrPinsAreSet_OrTheCaseIsASingleSectionOne()
    {
        // pins
        var inputs = Simple(ForcedChain());
        var pinned = AnalyzeAll(inputs, pins: new HashSet<string> { "C c2" });
        Assert.False(pinned.Reasons.HasSteps);
        // single-section case: one section per group
        var (_, single) = Run([Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(0, 9, 10))]);
        Assert.False(single.Reasons.HasSteps);
        Assert.Same(StrandedReasons.Empty, single.Reasons);
    }

    [Fact]
    public void AClusterWithTooManyCombinations_IsSkippedNotGuessedAt()
    {
        // 3 groups of 18 free sections: 5832 combinations of any three, over the cap; x conflicts with the same
        // time in every combination, but only by luck of enumeration order a truncated search would "prove" it
        var sections = new List<Section>();
        for (int g = 0; g < 3; g++)
            for (int i = 0; i < 18; i++)
                sections.Add(Sec("G" + g, "s" + i, Lecture, At(i % 5, 8 + i / 5, 9 + i / 5)));
        sections.Add(Sec("X", "x1", Lecture, At(0, 8, 9)));
        sections.Add(Sec("X", "x2", Lecture, At(0, 8.5, 9.5)));
        var (_, a) = Run(sections);
        // nothing is wrongly ruled out: every section of the G courses can be in a slate with some X
        Assert.All(a.Reasons.Steps, s => Assert.True(s.Kind == ReasonStepKind.Clash || s.Groups.Count >= 2));
        AssertSound(a);
    }

    // ── Soundness ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The probe's brute-force check: for every kind of counting slate (every skip set), the sections some filling of
    /// the other groups uses; nothing the narrative rules out may be one of them.
    /// </summary>
    private static void AssertSound(Analysis a)
    {
        foreach (var skip in a.CountingSkipSets)
        {
            var used = UsedByAFilling(a, skip);
            foreach (var step in a.Reasons.Steps)
                foreach (var s in step.RuledOut)
                {
                    int g = Enumerable.Range(0, a.Groups.Count).First(i => a.Groups[i].Candidates.Any(c => c.Id == s.Id));
                    if (skip.Contains(g)) continue;
                    Assert.DoesNotContain(s.Id, used);
                }
        }
    }

    /// <summary>The ids of the sections that some filling of every group except those in skip uses.</summary>
    private static HashSet<string> UsedByAFilling(Analysis a, IReadOnlyList<int> skip)
    {
        var order = Enumerable.Range(0, a.Groups.Count).Where(g => !skip.Contains(g)).ToList();
        var used = new HashSet<string>();
        var pick = new List<Section>();
        void Rec(int d)
        {
            if (d == order.Count) { foreach (var p in pick) used.Add(p.Id); return; }
            foreach (var s in a.Groups[order[d]].Candidates)
            {
                if (pick.Any(p => SectionOverlap.Conflicts(p, s) || (a.OneCampusRule && CampusRule.Splits(p, s)))) continue;
                pick.Add(s); Rec(d + 1); pick.RemoveAt(pick.Count - 1);
            }
        }
        Rec(0);
        return used;
    }

    [Fact]
    public void NothingRuledOut_IsInASlateThatCounts_OnTheHandBuiltCases()
    {
        foreach (var sections in new[] { ForcedChain(), SizeOneFootprint(), TwoGroupFootprintWithTimes(),
                     TwoGroupFootprintWithoutTimes(), AccessAnalyzerTests.ThreeCourseChain() })
        {
            var (_, a) = Run(sections);
            Assert.NotEmpty(a.Reasons.Steps);
            AssertSound(a);
        }
    }

    [Fact]
    public void NothingRuledOut_IsInASlateThatCounts_OnManyRandomSmallSchedules()
    {
        var rng = new Random(20261127);
        int explained = 0;
        for (int round = 0; round < 150; round++)
        {
            var sections = new List<Section>();
            int courses = rng.Next(3, 6);
            for (int c = 0; c < courses; c++)
                for (int i = 0, n = rng.Next(1, 5); i < n; i++)
                {
                    int day = rng.Next(0, 3), start = 9 + rng.Next(0, 3);
                    sections.Add(Sec("K" + c, "s" + i, Lecture, At(day, start, start + 1)));
                }
            var (_, a) = Run(sections);
            AssertSound(a);
            explained += a.Reasons.Steps.Sum(s => s.RuledOut.Count);
        }
        Assert.True(explained > 0, "the random schedules should rule some sections out");
    }

    // ── The wording ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void W1_AClash_SaysWhatItConflictsWithAndWhyItCantBeAvoided()
    {
        var (inputs, a) = Run(ForcedChain());
        Assert.Equal("it conflicts with A 1, A's only section.", AccessText.StrandedClause(inputs, a, Sect(inputs, "B b1")));
        Assert.Equal("it conflicts with B b2, the only B section they can take.", AccessText.StrandedClause(inputs, a, Sect(inputs, "C c1")));
        var (i2, a2) = Run(SizeOneFootprint());
        Assert.Equal("it conflicts with A a1 and a2, A's only sections.", AccessText.StrandedClause(i2, a2, Sect(i2, "B b1")));
    }

    [Fact]
    public void W1_AFootprint_NamesTheComponentsAndTheTimesTaken_OrJustThatOneConflicts()
    {
        var (inputs, a) = Run(TwoGroupFootprintWithTimes());
        Assert.Equal("whichever C section and D section they take, Mon and Tue 9:00–10:00 are taken.",
            AccessText.StrandedClause(inputs, a, Sect(inputs, "E x1")));
        var (i2, a2) = Run(AccessAnalyzerTests.ThreeCourseChain());
        Assert.Equal("whichever C section and D section they take, one of them conflicts with it.",
            AccessText.StrandedClause(i2, a2, Sect(i2, "B x")));
    }

    [Fact]
    public void W1_TheTileHoverAndTheFlag_SayTheSameClause_AndFallBackWithoutANarrative()
    {
        var (inputs, a) = Run(ForcedChain());
        var looks = GridAccessPicture.Build(inputs, a, "W", inputs.Sections, null, Cautions.For(a), false, id => id ?? "none");
        Assert.Equal("Students in **W** can't take B b1: it conflicts with A 1, A's only section.", looks["B b1"].Hover);
        Assert.Equal("B b1 is stranded: it conflicts with A 1, A's only section. No student taking all of W can use it.",
            AccessText.StrandedFlagText(inputs, a, Sect(inputs, "B b1"), "W", false, id => id ?? "none"));
        var courseOnly = GridAccessPicture.Build(inputs, a, "A", inputs.Sections, null, Cautions.For(a), true, id => id ?? "none");
        Assert.StartsWith("Students taking A can't take B b1: ", courseOnly["B b1"].Hover);

        // pinned: no narrative, so today's wording
        var pinned = AnalyzeAll(inputs, pins: new HashSet<string> { "C c2" });
        Assert.Null(AccessText.StrandedClause(inputs, pinned, Sect(inputs, "B b1")));
    }

    [Fact]
    public void W1_TheMergedMarkHover_NamesOneOrSeveralStrandedSections()
    {
        // A's only section A 1 (Monday 9) strands B 1 and B 2 (both Monday 9)
        var sections = new List<Section>
        {
            Sec("A", "1", Lecture, At(0, 9, 10)),
            Sec("B", "1", Lecture, At(0, 9, 10)), Sec("B", "2", Lecture, At(0, 9, 10)), Sec("B", "3", Lecture, At(1, 9, 10)),
        };
        var inputs = Simple(sections);
        var marks = ConflictMarks.For(inputs, inputs.Sections, false, false)
            .Select(m => new WatchMark("w", "Y2", m)).ToList();
        var tip = Assert.Single(AccessText.ConflictTips(inputs, sections[0], 1, marks));
        Assert.Equal("Students in **Y2** can't take B 1 or 2: they conflict with A 1, A's only section.", tip);
        var one = Assert.Single(AccessText.ConflictTips(inputs, sections[1], 1, marks));
        Assert.Equal("Students in **Y2** can't take B 1: it conflicts with A 1, A's only section.", one);
    }

    [Fact]
    public void W3_TheWhyList_OneNumberedLinePerStep()
    {
        var (inputs, a) = Run(ForcedChain());
        Assert.Equal(
            ["1. A 1 is A's only section, so students can't take B b1, which conflicts with it.",
             "2. That leaves B b2 as the only B section, which rules out C c1."],
            AccessText.WhyLines(inputs, a, Sect(inputs, "C c1")));
        Assert.Empty(AccessText.WhyLines(inputs, a, Sect(inputs, "B b1")));   // one step: the hover says it all
        Assert.Empty(AccessText.WhyLines(inputs, a, Sect(inputs, "C c2")));   // not stranded
    }

    [Fact]
    public void W3_TheWhyList_NarrowedGroupOfSeveral_SaysBoth()
    {
        // F's only section (Tuesday) rules out G g3; G g1 and g2 (Monday) are what is left, and both conflict with H h1
        var (inputs, a) = Run([
            Sec("F", "f1", Lecture, At(1, 9, 10)),
            Sec("G", "g1", Lecture, At(0, 9, 10)), Sec("G", "g2", Lecture, At(0, 9, 10)), Sec("G", "g3", Lecture, At(1, 9, 10)),
            Sec("H", "h1", Lecture, At(0, 9, 10)), Sec("H", "h2", Lecture, At(2, 9, 10)),
        ]);
        Assert.Equal(
            ["1. F f1 is F's only section, so students can't take G g3, which conflicts with it.",
             "2. That leaves G g1 and g2 as the only G sections, and both conflict with H h1."],
            AccessText.WhyLines(inputs, a, Sect(inputs, "H h1")));
        Assert.Equal("it conflicts with G g1 and g2, the only G sections they can take.",
            AccessText.StrandedClause(inputs, a, Sect(inputs, "H h1")));
    }

    [Fact]
    public void W3_TheWhyList_AFootprintLine_WithTimes_AndWithout()
    {
        var (inputs, a) = Run(TwoGroupFootprintWithTimes());
        Assert.Equal(
            ["1. Whichever C section and D section students take, Mon and Tue 9:00–10:00 are taken. That rules out E x1.",
             "2. That leaves E x2 as the only E section, which rules out Z z1."],
            AccessText.WhyLines(inputs, a, Sect(inputs, "Z z1")));

        var (i2, a2) = Run(TwoGroupFootprintWithoutTimes());
        Assert.Equal(
            ["1. Whichever C section and D section students take, one of them conflicts with B x.",
             "2. That leaves B y as the only B section, which rules out Z z1."],
            AccessText.WhyLines(i2, a2, Sect(i2, "Z z1")));
    }

    [Fact]
    public void CommonTimes_AreJoinedByRange_DaysSharingARangeTogether()
    {
        Assert.Equal("Mon, Tue and Wed 16:00–17:00",
            AccessText.CommonTimesText([new(1, 960, 1020), new(2, 960, 1020), new(3, 960, 1020)]));
        Assert.Equal("Mon 9:00–10:30 and Thu 14:00–15:00",
            AccessText.CommonTimesText([new(1, 540, 630), new(4, 840, 900)]));
    }

    // ── Moves ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void W2_AMoveOption_NamesTheSectionsItConflictsWithAndAQualifier()
    {
        // A 1 and B 1 clash with no full slate; each is forced when the other's component is left out
        var (inputs, a) = Run([Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(0, 9, 10)),
            Sec("C", "1", Lecture, At(3, 9, 10)), Sec("C", "2", Lecture, At(4, 9, 10))]);
        var moves = MoveSuggestions.For(a, null);
        var problem = Assert.Single(moves.Problems);
        var option = problem.Options.Single(o => o.Section.Id == "A 1");
        Assert.NotNull(option.Reason);
        Assert.Equal(["B 1"], Ids(option.Reason!.Against));
        Assert.False(option.Reason.Narrowed);
        Assert.Equal("A 1 conflicts with B 1, B's only section, so **W** has no full slate. Try moving A 1 or B 1.",
            AccessText.MoveTip(inputs, option.Section, option.ConflictsWith, problem, 1, "**W**"));
    }

    [Fact]
    public void W2_ANarrowedGroup_SaysTheOnlyOneStudentsCanStillTake()
    {
        // P 1 clashes with R 2, which is all that is left of R once S's only section (Tuesday) rules out R 1
        var (inputs, a) = Run([
            Sec("P", "1", Lecture, At(0, 9, 10)),
            Sec("R", "1", Lecture, At(1, 9, 10)), Sec("R", "2", Lecture, At(0, 9, 10)),
            Sec("S", "1", Lecture, At(1, 9, 10))]);
        var moves = MoveSuggestions.For(a, null);
        var problem = Assert.Single(moves.Problems);
        var option = problem.Options.Single(o => o.Section.Id == "P 1");
        Assert.True(option.Reason!.Narrowed);
        Assert.Equal(["R 2"], Ids(option.Reason.Against));
        Assert.StartsWith("P 1 conflicts with R 2, the only R section students can take, so **W** has no full slate. Try moving ",
            AccessText.MoveTip(inputs, option.Section, option.ConflictsWith, problem, 1, "**W**"));
    }

    [Fact]
    public void MoveWhy_ListsWhatTookTheOtherSectionsAway_ThenWhatIsLeft()
    {
        // As above: S's only section rules out R 1, leaving R 2, which P 1 conflicts with
        var (inputs, a) = Run([
            Sec("P", "1", Lecture, At(0, 9, 10)),
            Sec("R", "1", Lecture, At(1, 9, 10)), Sec("R", "2", Lecture, At(0, 9, 10)),
            Sec("S", "1", Lecture, At(1, 9, 10))]);
        var option = MoveSuggestions.For(a, null).Problems.Single().Options.Single(o => o.Section.Id == "P 1");
        Assert.Equal(
            ["1. S 1 is S's only section, so students can't take R 1, which conflicts with it.",
             "2. That leaves R 2, and P 1 conflicts with it."],
            AccessText.MoveWhyLines(inputs, a, option.Section, option.Reason!));
    }

    [Fact]
    public void MoveWhy_IsEmpty_WhenNothingWasRuledOutFirst()
    {
        var (inputs, a) = Run([Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(0, 9, 10))]);
        foreach (var option in MoveSuggestions.For(a, null).Problems.SelectMany(p => p.Options))
            if (option.Reason is { } reason) Assert.Empty(AccessText.MoveWhyLines(inputs, a, option.Section, reason));
    }

    [Fact]
    public void W2_ASectionWithNoMoveReason_KeepsTodaysText()
    {
        var a1 = Sec("A", "1", Lecture, At(0, 9, 10)); var b1 = Sec("B", "1", Lecture, At(0, 9, 10));
        var inputs = Simple([a1, b1]);
        var problem = new MoveProblem([new MoveOption(a1, [b1])]);
        Assert.Equal("A 1 conflicts with B 1, so **W** has no full slate. Try moving A 1.",
            AccessText.MoveTip(inputs, a1, [b1], problem, 1, "**W**"));
    }
}
