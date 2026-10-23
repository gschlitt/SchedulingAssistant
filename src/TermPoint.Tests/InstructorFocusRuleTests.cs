using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Pure tests of <see cref="InstructorFocusRule"/> (spec item 20, step 20.1): every row of the design document's
/// examples table, plus the corner cases of the six-step rule. The viewed year (Y) throughout is 2025-2026, so
/// the window is 2024-2025 and 2025-2026; 2023-2024 and earlier are "earlier years".
/// </summary>
public sealed class InstructorFocusRuleTests
{
    private const string Biol = "BIOL";
    private const string Engl = "ENGL";
    private const string Chem = "CHEM";

    /// <summary>The viewed year used by most tests.</summary>
    private const string Viewed = "2025-2026";

    private static readonly SubjectFocus Biology = SubjectFocus.ForSubjects([Biol]);
    private static readonly SubjectFocus English = SubjectFocus.ForSubjects([Engl]);
    private static readonly SubjectFocus Chemistry = SubjectFocus.ForSubjects([Chem]);

    /// <summary>A world with every year from 2020-2021 to 2026-2027 present (so no year is "unknown").</summary>
    private static FocusWorld WorldWithYears()
    {
        var world = new FocusWorld();
        foreach (var name in new[]
                 { "2020-2021", "2021-2022", "2022-2023", "2023-2024", "2024-2025", "2025-2026", "2026-2027" })
            world.Year(name);
        return world;
    }

    /// <summary>Runs the rule for one instructor under one focus against the world's history.</summary>
    private static bool InFocus(FocusWorld world, SubjectFocus focus, Instructor instructor, string? viewedYearName = Viewed) =>
        InstructorFocusRule.IsInFocus(
            focus, instructor, world.Build(), viewedYearName is null ? null : world.YearId(viewedYearName));

    // ── The examples table (focus = Biology) ────────────────────────────────────

    /// <summary>Row 1: teaches BIOL this year: step 4, in focus.</summary>
    [Fact]
    public void Table_TeachesFocusSubjectThisYear_InFocus()
    {
        var w = WorldWithYears();
        w.Teach("i", Biol, "2025-2026");
        Assert.True(InFocus(w, Biology, FocusWorld.Instructor("i")));
    }

    /// <summary>Row 1: teaches BIOL last year: step 4 (the window includes Y-1), in focus.</summary>
    [Fact]
    public void Table_TeachesFocusSubjectLastYear_InFocus()
    {
        var w = WorldWithYears();
        w.Teach("i", Biol, "2024-2025");
        Assert.True(InFocus(w, Biology, FocusWorld.Instructor("i")));
    }

    /// <summary>Row 2: BIOL and ENGL in the window: in focus under Biology and under English.</summary>
    [Fact]
    public void Table_TeachesBothSubjectsInWindow_InFocusUnderEither()
    {
        var w = WorldWithYears();
        w.Teach("i", Biol, "2025-2026");
        w.Teach("i", Engl, "2024-2025");
        var i = FocusWorld.Instructor("i");
        Assert.True(InFocus(w, Biology, i));
        Assert.True(InFocus(w, English, i));
        Assert.False(InFocus(w, Chemistry, i));
    }

    /// <summary>Row 3: only ENGL in the window, BIOL four years ago: step 4 ignores earlier years, not in focus.</summary>
    [Fact]
    public void Table_OnlyOtherSubjectInWindow_BiolFourYearsAgo_NotInFocus()
    {
        var w = WorldWithYears();
        w.Teach("i", Engl, "2025-2026");
        w.Teach("i", Biol, "2021-2022");
        Assert.False(InFocus(w, Biology, FocusWorld.Instructor("i")));
    }

    /// <summary>Row 4: taught BIOL in 2022-23, nothing since: step 5, in focus.</summary>
    [Fact]
    public void Table_TaughtBiolBeforeWindow_NothingSince_InFocus()
    {
        var w = WorldWithYears();
        w.Teach("i", Biol, "2022-2023");
        Assert.True(InFocus(w, Biology, FocusWorld.Instructor("i")));
    }

    /// <summary>Row 5: idle ENGL sessional, last taught 2021-22, still active: step 5, in focus only under English.</summary>
    [Fact]
    public void Table_IdleEnglishSessional_OnlyUnderEnglish()
    {
        var w = WorldWithYears();
        w.Teach("i", Engl, "2021-2022");
        var i = FocusWorld.Instructor("i");
        Assert.False(InFocus(w, Biology, i));
        Assert.True(InFocus(w, English, i));
    }

    /// <summary>Row 6: a new hire with no sections anywhere: step 6, in focus under every focus.</summary>
    [Fact]
    public void Table_NewHire_InFocusUnderEverySubjectFocus()
    {
        var w = WorldWithYears();
        var i = FocusWorld.Instructor("new");
        Assert.True(InFocus(w, Biology, i));
        Assert.True(InFocus(w, English, i));
        Assert.True(InFocus(w, SubjectFocus.ForSubjects([Biol, Engl, Chem]), i));
    }

    /// <summary>Row 7: a new hire given a first CHEM section: step 4, now only under Chemistry.</summary>
    [Fact]
    public void Table_NewHireGivenFirstChemSection_OnlyUnderChemistry()
    {
        var w = WorldWithYears();
        w.Teach("new", Chem, "2025-2026");
        var i = FocusWorld.Instructor("new");
        Assert.False(InFocus(w, Biology, i));
        Assert.True(InFocus(w, Chemistry, i));
    }

    /// <summary>Row 8: an inactive instructor who teaches BIOL: step 3, not in focus.</summary>
    [Fact]
    public void Table_Inactive_NotInFocus()
    {
        var w = WorldWithYears();
        w.Teach("i", Biol, "2025-2026");
        Assert.False(InFocus(w, Biology, FocusWorld.Instructor("i", isActive: false)));
    }

    // ── Steps 1-3 ──────────────────────────────────────────────────────────────

    /// <summary>Step 1: All is in focus for everyone, and it is decided BEFORE the inactive check (the design says so).</summary>
    [Fact]
    public void All_IsInFocus_EvenForInactive()
    {
        var w = WorldWithYears();
        Assert.True(InFocus(w, SubjectFocus.All, FocusWorld.Instructor("i")));
        Assert.True(InFocus(w, SubjectFocus.All, FocusWorld.Instructor("gone", isActive: false)));
    }

    /// <summary>Step 2: None is in focus for no one, even a new hire.</summary>
    [Fact]
    public void None_IsNeverInFocus()
    {
        var w = WorldWithYears();
        w.Teach("i", Biol, "2025-2026");
        Assert.False(InFocus(w, SubjectFocus.None, FocusWorld.Instructor("i")));
        Assert.False(InFocus(w, SubjectFocus.None, FocusWorld.Instructor("new")));
    }

    // ── Step 4 ─────────────────────────────────────────────────────────────────

    /// <summary>A focus of two subjects matches an instructor who teaches either one in the window.</summary>
    [Fact]
    public void TwoSubjectFocus_MatchesEitherSubject()
    {
        var w = WorldWithYears();
        w.Teach("b", Biol, "2025-2026");
        w.Teach("e", Engl, "2025-2026");
        w.Teach("c", Chem, "2025-2026");
        var focus = SubjectFocus.ForSubjects([Biol, Engl]);
        Assert.True(InFocus(w, focus, FocusWorld.Instructor("b")));
        Assert.True(InFocus(w, focus, FocusWorld.Instructor("e")));
        Assert.False(InFocus(w, focus, FocusWorld.Instructor("c")));
    }

    /// <summary>The window is WHOLE years: BIOL taught only in the Winter semester of Y counts even if Fall is what is viewed.</summary>
    [Fact]
    public void AnotherSemesterOfTheViewedYear_Counts()
    {
        var w = WorldWithYears();
        w.Teach("i", Engl, "2024-2025");                    // would otherwise decide: not in focus
        w.Teach("i", Biol, "2025-2026", semesterName: "Winter");
        Assert.True(InFocus(w, Biology, FocusWorld.Instructor("i")));
    }

    /// <summary>Only the previous year in the window, with another subject there and BIOL earlier: step 4 decides, not in focus.</summary>
    [Fact]
    public void PreviousYearOtherSubject_BiolBeforeThat_NotInFocus()
    {
        var w = WorldWithYears();
        w.Teach("i", Engl, "2024-2025");
        w.Teach("i", Biol, "2022-2023");
        Assert.False(InFocus(w, Biology, FocusWorld.Instructor("i")));
    }

    // ── Step 5 ─────────────────────────────────────────────────────────────────

    /// <summary>Step 5 looks only at the MOST RECENT earlier year: BIOL long ago does not rescue a recent ENGL year.</summary>
    [Fact]
    public void EarlierYears_OnlyTheMostRecentOneDecides()
    {
        var w = WorldWithYears();
        w.Teach("i", Biol, "2020-2021");
        w.Teach("i", Engl, "2023-2024");    // most recent before the window
        var i = FocusWorld.Instructor("i");
        Assert.False(InFocus(w, Biology, i));
        Assert.True(InFocus(w, English, i));
    }

    /// <summary>In that most recent earlier year, any one matching subject is enough.</summary>
    [Fact]
    public void EarlierYear_AnyMatchingSubjectThatYear_InFocus()
    {
        var w = WorldWithYears();
        w.Teach("i", Engl, "2023-2024");
        w.Teach("i", Biol, "2023-2024", semesterName: "Winter");
        Assert.True(InFocus(w, Biology, FocusWorld.Instructor("i")));
    }

    // ── Step 6 ─────────────────────────────────────────────────────────────────

    /// <summary>Sections only in a year AFTER Y (viewing a past year) fall through to step 6: in focus under every focus.</summary>
    [Fact]
    public void SectionsOnlyAfterViewedYear_InFocus()
    {
        var w = WorldWithYears();
        w.Teach("i", Engl, "2026-2027");
        var i = FocusWorld.Instructor("i");
        Assert.True(InFocus(w, Biology, i));
        Assert.True(InFocus(w, English, i));
    }

    /// <summary>A year after Y never counts as "earlier": ENGL in 2026-27 plus BIOL in 2022-23 is decided by the BIOL year (step 5).</summary>
    [Fact]
    public void YearAfterViewed_IsIgnoredByStepFive()
    {
        var w = WorldWithYears();
        w.Teach("i", Engl, "2026-2027");
        w.Teach("i", Biol, "2022-2023");
        var i = FocusWorld.Instructor("i");
        Assert.True(InFocus(w, Biology, i));
        Assert.False(InFocus(w, English, i));
    }

    // ── Unknown viewed year ────────────────────────────────────────────────────

    /// <summary>With no viewed year the window is empty, so the most recent year with any section decides (all years are "earlier").</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("no-such-year")]
    public void UnknownViewedYear_MostRecentYearOverallDecides(string? viewedYearId)
    {
        var w = WorldWithYears();
        w.Teach("recentEngl", Engl, "2025-2026");
        w.Teach("recentEngl", Biol, "2021-2022");
        w.Teach("recentBiol", Biol, "2025-2026");
        w.Teach("recentBiol", Engl, "2021-2022");
        var history = w.Build();

        Assert.False(InstructorFocusRule.IsInFocus(Biology, FocusWorld.Instructor("recentEngl"), history, viewedYearId));
        Assert.True(InstructorFocusRule.IsInFocus(Biology, FocusWorld.Instructor("recentBiol"), history, viewedYearId));
    }

    /// <summary>With no viewed year, a new hire (no sections at all) is still step 6: in focus.</summary>
    [Fact]
    public void UnknownViewedYear_NewHire_InFocus()
    {
        var w = WorldWithYears();
        Assert.True(InFocus(w, Biology, FocusWorld.Instructor("new"), viewedYearName: null));
    }

    // ── Years are positions, not start years ───────────────────────────────────

    /// <summary>
    /// "The year before Y" is the previous year that EXISTS, so a gap does not break the window: with years
    /// 2020-2021 and 2023-2024 only, viewing 2023-2024 puts 2020-2021 in the window (BIOL there counts, step 4).
    /// </summary>
    [Fact]
    public void YearGap_PreviousExistingYearIsInTheWindow()
    {
        var w = new FocusWorld();
        w.Year("2020-2021");
        w.Year("2023-2024");
        w.Teach("i", Biol, "2020-2021");
        w.Teach("i", Engl, "2023-2024");
        Assert.True(InFocus(w, Biology, FocusWorld.Instructor("i"), "2023-2024"));
    }

    /// <summary>
    /// The same gap, the other way: with three years 2019-2020, 2020-2021, 2023-2024, viewing 2023-2024 the window is
    /// 2020-2021 and 2023-2024, so BIOL in 2019-2020 is an earlier year (step 4 ignores it: ENGL in the window decides).
    /// </summary>
    [Fact]
    public void YearGap_YearBeforeThePreviousOneIsEarlier()
    {
        var w = new FocusWorld();
        w.Year("2019-2020");
        w.Year("2020-2021");
        w.Year("2023-2024");
        w.Teach("i", Biol, "2019-2020");
        w.Teach("i", Engl, "2020-2021");
        Assert.False(InFocus(w, Biology, FocusWorld.Instructor("i"), "2023-2024"));
    }

    /// <summary>Viewing the earliest year: there is no previous year, and later years are not "earlier" (step 6).</summary>
    [Fact]
    public void ViewingEarliestYear_NoPreviousYear()
    {
        var w = WorldWithYears();
        w.Teach("i", Engl, "2022-2023");
        Assert.True(InFocus(w, Biology, FocusWorld.Instructor("i"), "2020-2021"));
    }
}
