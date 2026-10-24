using TermPoint.Models;
using TermPoint.Services.SectionImport;
using Xunit;
using static TermPoint.Tests.SectionImportTestKit;
using C = TermPoint.Services.SectionImport.SectionImportColumns;

namespace TermPoint.Tests;

/// <summary>
/// Tests for the meeting-time rules of <see cref="SectionImportPlanner"/> (step 24.4, design Phase 4's
/// "Meeting-time rules"): blank or complete meeting cells, days, weekend days, the 07:30 and 22:00
/// limits, End after Begin, frequency, and expanding a row into one meeting per day.
/// </summary>
public class SectionImportMeetingRulesTests
{
    /// <summary>Plans a single row of CHEM101 A that meets as given.</summary>
    private static ImportReport PlanMeeting(string days, string begin, string end, string frequency = "",
        bool includeSaturday = false, bool includeSunday = false) =>
        SectionImportPlanner.Plan(
            Sheet(Mtg(2, "CHEM", "101", "A", days, begin, end, (C.Frequency, frequency))),
            Snapshot(includeSaturday: includeSaturday, includeSunday: includeSunday));

    // ── Blank and partly filled ───────────────────────────────────────────────

    /// <summary>All three blank is a section with no meeting time: no problem, no meeting.</summary>
    [Fact]
    public void AllThreeBlank_IsNoMeeting_AndNoProblem()
    {
        var report = PlanMeeting("", "", "");

        Assert.Empty(report.Problems);
        Assert.Empty(report.Plan.NewSections.Single().Meetings);
    }

    /// <summary>A frequency on a row with no meeting time is ignored, not checked.</summary>
    [Fact]
    public void AFrequencyOnARowWithNoMeeting_IsIgnored()
    {
        var report = PlanMeeting("", "", "", frequency: "sometimes");

        Assert.Empty(report.Problems);
    }

    /// <summary>Each way of filling only some of Meeting Days, Begin and End is a problem naming what is missing.</summary>
    [Theory]
    [InlineData("M", "", "", C.Begin, "Begin and End are empty; fill in all three of Meeting Days, Begin and End, or leave all three blank.")]
    [InlineData("", "1000", "", C.MeetingDays, "Meeting Days and End are empty; fill in all three of Meeting Days, Begin and End, or leave all three blank.")]
    [InlineData("", "", "1100", C.MeetingDays, "Meeting Days and Begin are empty; fill in all three of Meeting Days, Begin and End, or leave all three blank.")]
    [InlineData("M", "1000", "", C.End, "End is empty; fill in all three of Meeting Days, Begin and End, or leave all three blank.")]
    [InlineData("M", "", "1100", C.Begin, "Begin is empty; fill in all three of Meeting Days, Begin and End, or leave all three blank.")]
    [InlineData("", "1000", "1100", C.MeetingDays, "Meeting Days is empty; fill in all three of Meeting Days, Begin and End, or leave all three blank.")]
    public void PartlyFilled_IsAProblemNamingWhatIsMissing(string days, string begin, string end, string column, string message)
    {
        var report = PlanMeeting(days, begin, end);

        var problem = Assert.Single(report.Problems);
        Assert.Equal(2, problem.Row);
        Assert.Equal(column, problem.Column);
        Assert.Equal(message, problem.Message);
        Assert.Empty(report.Plan.NewSections.Single().Meetings);
    }

    /// <summary>A partly filled row still has its filled cells checked, so every problem is reported.</summary>
    [Fact]
    public void PartlyFilled_StillChecksTheCellsThatAreFilled()
    {
        var report = PlanMeeting("Xyz", "", "");

        Assert.Equal(new[] { C.Begin, C.MeetingDays }, report.Problems.Select(p => p.Column!).OrderBy(c => c).ToArray());
    }

    // ── Days ──────────────────────────────────────────────────────────────────

    /// <summary>A day that cannot be read is a problem in Meeting Days.</summary>
    [Fact]
    public void BadDays_AreAProblemInMeetingDays()
    {
        var report = PlanMeeting("Xyz", "1000", "1100");

        var problem = Assert.Single(report.Problems);
        Assert.Equal(C.MeetingDays, problem.Column);
        Assert.Contains("isn't a day", problem.Message);
    }

    /// <summary>A day listed twice is a problem too.</summary>
    [Fact]
    public void ADayListedTwice_IsAProblem()
    {
        var report = PlanMeeting("M, Mon", "1000", "1100");

        Assert.Equal("Monday is listed twice.", Assert.Single(report.Problems).Message);
    }

    /// <summary>Saturday and Sunday are problems while they are turned off, naming where to turn them on.</summary>
    [Theory]
    [InlineData("Sa", "Saturday")]
    [InlineData("Su", "Sunday")]
    public void WeekendDay_WhenOff_IsAProblem(string days, string dayName)
    {
        var report = PlanMeeting(days, "1000", "1100");

        var problem = Assert.Single(report.Problems);
        Assert.Equal(C.MeetingDays, problem.Column);
        Assert.Equal($"{dayName} isn't turned on in TermPoint. Turn it on in Configuration, under Scheduling, then check again.",
            problem.Message);
        Assert.Empty(report.Plan.NewSections.Single().Meetings);
    }

    /// <summary>With both weekend days turned on, they are day 6 and day 7.</summary>
    [Fact]
    public void WeekendDays_WhenOn_AreAccepted()
    {
        var report = PlanMeeting("Sa Su", "1000", "1100", includeSaturday: true, includeSunday: true);

        Assert.Empty(report.Problems);
        Assert.Equal(new[] { 6, 7 }, report.Plan.NewSections.Single().Meetings.Select(m => m.Day).ToArray());
    }

    /// <summary>Saturday on does not turn Sunday on.</summary>
    [Fact]
    public void SaturdayOn_DoesNotAllowSunday()
    {
        var report = PlanMeeting("Sa,Su", "1000", "1100", includeSaturday: true);

        var problem = Assert.Single(report.Problems);
        Assert.StartsWith("Sunday isn't turned on", problem.Message);
    }

    // ── Times and limits ──────────────────────────────────────────────────────

    /// <summary>A Begin or End that is not a time is a problem in its own column.</summary>
    [Fact]
    public void BadTimes_AreProblemsInTheirOwnColumns()
    {
        var report = PlanMeeting("M", "25:00", "later");

        Assert.Equal(new[] { C.Begin, C.End }, report.Problems.Select(p => p.Column!).OrderBy(c => c).ToArray());
    }

    /// <summary>Begin 07:30 is the earliest allowed; 07:29 is a problem.</summary>
    [Fact]
    public void Begin_BothSidesOfTheEarliestLimit()
    {
        Assert.Empty(PlanMeeting("M", "0730", "0900").Problems);

        var problem = Assert.Single(PlanMeeting("M", "0729", "0900").Problems);
        Assert.Equal(C.Begin, problem.Column);
        Assert.Equal("Begin 0729 is before 0730, the earliest time TermPoint shows.", problem.Message);
    }

    /// <summary>End 22:00 is the latest allowed; 22:01 is a problem.</summary>
    [Fact]
    public void End_BothSidesOfTheLatestLimit()
    {
        Assert.Empty(PlanMeeting("M", "2000", "2200").Problems);

        var problem = Assert.Single(PlanMeeting("M", "2000", "2201").Problems);
        Assert.Equal(C.End, problem.Column);
        Assert.Equal("End 2201 is after 2200, the latest time TermPoint shows.", problem.Message);
    }

    /// <summary>The message shows times in the four-digit form even when the cell was typed another way.</summary>
    [Fact]
    public void LimitMessages_UseFourDigitTimes()
    {
        var problem = Assert.Single(PlanMeeting("M", "7:00 AM", "9:00 AM").Problems);

        Assert.Equal("Begin 0700 is before 0730, the earliest time TermPoint shows.", problem.Message);
    }

    /// <summary>End equal to Begin, or before it, is a problem in End.</summary>
    [Theory]
    [InlineData("1000", "1000")]
    [InlineData("1100", "1000")]
    public void EndNotAfterBegin_IsAProblem(string begin, string end)
    {
        var problem = Assert.Single(PlanMeeting("M", begin, end).Problems);

        Assert.Equal(C.End, problem.Column);
        Assert.Equal("End must be after Begin.", problem.Message);
    }

    // ── Frequency ─────────────────────────────────────────────────────────────

    /// <summary>A frequency that is neither weekly, odd, even nor week numbers is a problem in Frequency.</summary>
    [Fact]
    public void BadFrequency_IsAProblemInFrequency()
    {
        var problem = Assert.Single(PlanMeeting("M", "1000", "1100", "sometimes").Problems);

        Assert.Equal(C.Frequency, problem.Column);
        Assert.Contains("isn't a frequency", problem.Message);
    }

    /// <summary>Blank and weekly are weekly (null); odd and even are kept; week numbers are sorted.</summary>
    [Theory]
    [InlineData("", null)]
    [InlineData("Weekly", null)]
    [InlineData("odd", "odd")]
    [InlineData("Even Weeks", "even")]
    [InlineData("1,6,7", "1,6,7")]
    [InlineData("7, 1,6", "1,6,7")]
    public void Frequency_IsStoredInTheEditorsForm(string typed, string? stored)
    {
        var report = PlanMeeting("M", "1000", "1100", typed);

        Assert.Empty(report.Problems);
        Assert.Equal(stored, report.Plan.NewSections.Single().Meetings.Single().Frequency);
    }

    /// <summary>A repeated or zero custom week is a problem.</summary>
    [Theory]
    [InlineData("1,1")]
    [InlineData("0,2")]
    public void BadCustomWeeks_AreAProblem(string typed)
    {
        var problem = Assert.Single(PlanMeeting("M", "1000", "1100", typed).Problems);

        Assert.Equal(C.Frequency, problem.Column);
    }

    // ── Expansion ─────────────────────────────────────────────────────────────

    /// <summary>A row with two days becomes two meetings, each with the row's times, type and room.</summary>
    [Fact]
    public void ARowWithTwoDays_BecomesTwoMeetings()
    {
        var lab = Val("Lab");
        var room = Rm("SCI", "210");
        var report = SectionImportPlanner.Plan(
            Sheet(Mtg(7, "CHEM", "101", "A", "M,W", "1000", "1200",
                (C.MeetingType, "Lab"), (C.Building, "SCI"), (C.Room, "210"))),
            Snapshot(meetingTypes: new[] { lab }, rooms: new[] { room }));

        Assert.Empty(report.Problems);
        var meetings = report.Plan.NewSections.Single().Meetings;
        Assert.Equal(2, meetings.Count);
        Assert.Equal(new[] { 1, 3 }, meetings.Select(m => m.Day).ToArray());
        Assert.All(meetings, m =>
        {
            Assert.Equal(7, m.RowNumber);
            Assert.Equal(600, m.StartMinutes);
            Assert.Equal(120, m.DurationMinutes);
            Assert.Equal(720, m.EndMinutes);
            Assert.Null(m.Frequency);
            Assert.Same(lab, m.MeetingType!.Record);
            Assert.Same(room, m.Room!.Record);
        });
    }

    /// <summary>Several rows of one section give their meetings in file order, each with its own row number and frequency.</summary>
    [Fact]
    public void SeveralRows_GiveTheirMeetingsInFileOrder()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Mtg(2, "CHEM", "101", "A", "M", "1000", "1100"),
                Mtg(3, "CHEM", "101", "A", "F", "1400", "1550", (C.Frequency, "odd"))),
            Snapshot());

        var meetings = report.Plan.NewSections.Single().Meetings;
        Assert.Equal(new[] { 2, 3 }, meetings.Select(m => m.RowNumber).ToArray());
        Assert.Equal(new[] { 1, 5 }, meetings.Select(m => m.Day).ToArray());
        Assert.Equal(110, meetings[1].DurationMinutes);
        Assert.Equal("odd", meetings[1].Frequency);
    }

    /// <summary>A row with a problem makes no meetings, but the other rows of the section still do.</summary>
    [Fact]
    public void ARowWithAProblem_MakesNoMeetings()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Mtg(2, "CHEM", "101", "A", "M", "1000", "1100"),
                Mtg(3, "CHEM", "101", "A", "W", "0700", "0800")),
            Snapshot());

        Assert.Single(report.Problems);
        Assert.Equal(new[] { 2 }, report.Plan.NewSections.Single().Meetings.Select(m => m.RowNumber).ToArray());
    }
}
