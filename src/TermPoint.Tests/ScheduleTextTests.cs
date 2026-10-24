using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Unit tests for <see cref="ScheduleText"/>: day lists, times, week lists and frequencies as
/// people type them, including the plain-English error messages.
/// </summary>
public class ScheduleTextTests
{
    // ── Days ──────────────────────────────────────────────────────────────────

    /// <summary>Every day name and alias maps to the right day number, in any case.</summary>
    [Theory]
    [InlineData("Monday", 1)] [InlineData("Mon", 1)] [InlineData("Mo", 1)] [InlineData("M", 1)]
    [InlineData("Tuesday", 2)] [InlineData("Tue", 2)] [InlineData("Tu", 2)] [InlineData("T", 2)]
    [InlineData("Wednesday", 3)] [InlineData("Wed", 3)] [InlineData("We", 3)] [InlineData("W", 3)]
    [InlineData("Thursday", 4)] [InlineData("Thu", 4)] [InlineData("Th", 4)] [InlineData("R", 4)]
    [InlineData("Friday", 5)] [InlineData("Fri", 5)] [InlineData("Fr", 5)] [InlineData("F", 5)]
    [InlineData("Saturday", 6)] [InlineData("Sat", 6)] [InlineData("Sa", 6)]
    [InlineData("Sunday", 7)] [InlineData("Sun", 7)] [InlineData("Su", 7)]
    [InlineData("MONDAY", 1)] [InlineData("thursday", 4)] [InlineData("tH", 4)] [InlineData("r", 4)]
    public void ParseDays_EachAlias_MapsToDayNumber(string text, int expected)
    {
        var result = ScheduleText.ParseDays(text);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(new[] { expected }, result.Value);
    }

    /// <summary>Compact runs are split greedily, two-letter names first.</summary>
    [Theory]
    [InlineData("MWF", new[] { 1, 3, 5 })]
    [InlineData("TR", new[] { 2, 4 })]
    [InlineData("TTh", new[] { 2, 4 })]
    [InlineData("TuTh", new[] { 2, 4 })]
    [InlineData("MTWThF", new[] { 1, 2, 3, 4, 5 })]
    [InlineData("mtwthf", new[] { 1, 2, 3, 4, 5 })]
    [InlineData("SaSu", new[] { 6, 7 })]
    [InlineData("MoWeFr", new[] { 1, 3, 5 })]
    public void ParseDays_CompactRuns_SplitGreedily(string text, int[] expected)
    {
        var result = ScheduleText.ParseDays(text);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(expected, result.Value);
    }

    /// <summary>Commas, spaces or both separate day tokens, and the order given is kept.</summary>
    [Theory]
    [InlineData("M, W", new[] { 1, 3 })]
    [InlineData("M W", new[] { 1, 3 })]
    [InlineData("m,w", new[] { 1, 3 })]
    [InlineData("  F ,  M  ", new[] { 5, 1 })]
    [InlineData("Tue, Thu", new[] { 2, 4 })]
    [InlineData("Monday Wednesday,Friday", new[] { 1, 3, 5 })]
    [InlineData("MW F", new[] { 1, 3, 5 })]
    public void ParseDays_MixedSeparators_AreAccepted(string text, int[] expected)
    {
        var result = ScheduleText.ParseDays(text);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(expected, result.Value);
    }

    /// <summary>Blank text is an error asking for days.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" , ")]
    public void ParseDays_Blank_IsError(string? text)
    {
        var result = ScheduleText.ParseDays(text);

        Assert.False(result.IsValid);
        Assert.StartsWith("Enter the meeting days", result.Error);
    }

    /// <summary>An unknown token is quoted as typed, including "S" (ambiguous) and a compact run with a bad part.</summary>
    [Theory]
    [InlineData("X", "\"X\" isn't a day. Use M, T, W, R (or Th), F, Sa, Su, or the full day names.")]
    [InlineData("S", "\"S\" isn't a day. Use M, T, W, R (or Th), F, Sa, Su, or the full day names.")]
    [InlineData("MWX", "\"MWX\" isn't a day. Use M, T, W, R (or Th), F, Sa, Su, or the full day names.")]
    [InlineData("M x", "\"x\" isn't a day. Use M, T, W, R (or Th), F, Sa, Su, or the full day names.")]
    [InlineData("M3", "\"M3\" isn't a day. Use M, T, W, R (or Th), F, Sa, Su, or the full day names.")]
    public void ParseDays_UnknownToken_IsError(string text, string expectedError)
    {
        var result = ScheduleText.ParseDays(text);

        Assert.False(result.IsValid);
        Assert.Equal(expectedError, result.Error);
    }

    /// <summary>A day listed twice is an error naming the day, whether spelled the same way or not.</summary>
    [Theory]
    [InlineData("M M", "Monday is listed twice.")]
    [InlineData("Mon, M", "Monday is listed twice.")]
    [InlineData("MWM", "Monday is listed twice.")]
    [InlineData("TuT", "Tuesday is listed twice.")]
    [InlineData("Th R", "Thursday is listed twice.")]
    public void ParseDays_RepeatedDay_IsError(string text, string expectedError)
    {
        var result = ScheduleText.ParseDays(text);

        Assert.False(result.IsValid);
        Assert.Equal(expectedError, result.Error);
    }

    // ── Times ─────────────────────────────────────────────────────────────────

    /// <summary>Every accepted time form gives the right minutes from midnight.</summary>
    [Theory]
    [InlineData("930", 9 * 60 + 30)]
    [InlineData("0930", 9 * 60 + 30)]
    [InlineData("1000", 10 * 60)]
    [InlineData("1345", 13 * 60 + 45)]
    [InlineData("0000", 0)]
    [InlineData("2359", 23 * 60 + 59)]
    [InlineData("10:00", 10 * 60)]
    [InlineData("9:30", 9 * 60 + 30)]
    [InlineData("09:30", 9 * 60 + 30)]
    [InlineData("13:45", 13 * 60 + 45)]
    [InlineData("10:00:00", 10 * 60)]
    [InlineData("0:00", 0)]
    [InlineData("10:00 AM", 10 * 60)]
    [InlineData("10:00AM", 10 * 60)]
    [InlineData("10:00 am", 10 * 60)]
    [InlineData("1:30pm", 13 * 60 + 30)]
    [InlineData("1:30 PM", 13 * 60 + 30)]
    [InlineData("10 AM", 10 * 60)]
    [InlineData("9am", 9 * 60)]
    [InlineData("4 pm", 16 * 60)]
    [InlineData("12:00 PM", 12 * 60)]
    [InlineData("12 pm", 12 * 60)]
    [InlineData("12:30 PM", 12 * 60 + 30)]
    [InlineData("12:00 AM", 0)]
    [InlineData("12:15 am", 15)]
    [InlineData("  10:00  ", 10 * 60)]
    public void ParseTime_AcceptedForms_GiveMinutes(string text, int expected)
    {
        var result = ScheduleText.ParseTime(text);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(expected, result.Value);
    }

    /// <summary>Out-of-range, malformed and blank times are rejected, quoting what was typed.</summary>
    [Theory]
    [InlineData("2400")]
    [InlineData("1260")]
    [InlineData("25:00")]
    [InlineData("13:00 PM")]
    [InlineData("0:60")]
    [InlineData("0 AM")]
    [InlineData("13 PM")]
    [InlineData("10:00:30")]
    [InlineData("abc")]
    [InlineData("10")]
    [InlineData("10.30")]
    public void ParseTime_BadText_IsError(string text)
    {
        var result = ScheduleText.ParseTime(text);

        Assert.False(result.IsValid);
        Assert.Equal($"\"{text}\" isn't a time. Use a time like 1000, 10:00 or 10:00 AM.", result.Error);
    }

    /// <summary>Blank time text asks for a time rather than quoting nothing.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ParseTime_Blank_IsError(string? text)
    {
        var result = ScheduleText.ParseTime(text);

        Assert.False(result.IsValid);
        Assert.Equal("Enter a time like 1000, 10:00 or 10:00 AM.", result.Error);
    }

    /// <summary>The scheduling limits are the caller's job: early and late times parse fine.</summary>
    [Fact]
    public void ParseTime_DoesNotApplySchedulingLimits()
    {
        Assert.Equal(6 * 60, ScheduleText.ParseTime("0600").Value);
        Assert.Equal(23 * 60, ScheduleText.ParseTime("11 PM").Value);
    }

    // ── Week lists ────────────────────────────────────────────────────────────

    /// <summary>Week lists come back sorted ascending, comma-joined with no spaces.</summary>
    [Theory]
    [InlineData("6, 1,7", "1,6,7")]
    [InlineData("1", "1")]
    [InlineData("3,2,1", "1,2,3")]
    [InlineData(" 10 , 2 ", "2,10")]
    [InlineData("1,,2", "1,2")]
    [InlineData("1,3,5,", "1,3,5")]
    public void ParseWeekList_Valid_IsSortedAndJoined(string text, string expected)
    {
        var result = ScheduleText.ParseWeekList(text);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(expected, result.Value);
    }

    /// <summary>The three messages are the meeting editor's, word for word.</summary>
    [Theory]
    [InlineData(null, "Enter week numbers, e.g. 1,3,5")]
    [InlineData("", "Enter week numbers, e.g. 1,3,5")]
    [InlineData("   ", "Enter week numbers, e.g. 1,3,5")]
    [InlineData("a", "Each week must be a whole number ≥ 1")]
    [InlineData("1,x", "Each week must be a whole number ≥ 1")]
    [InlineData("1.5", "Each week must be a whole number ≥ 1")]
    [InlineData("0", "Each week must be a whole number ≥ 1")]
    [InlineData("-3", "Each week must be a whole number ≥ 1")]
    [InlineData("1,1", "Duplicate week numbers")]
    [InlineData("3, 1, 3", "Duplicate week numbers")]
    // The first bad token, left to right, decides the message.
    [InlineData("1,1,x", "Duplicate week numbers")]
    [InlineData("x,1,1", "Each week must be a whole number ≥ 1")]
    public void ParseWeekList_Invalid_UsesEditorMessages(string? text, string expectedError)
    {
        var result = ScheduleText.ParseWeekList(text);

        Assert.False(result.IsValid);
        Assert.Equal(expectedError, result.Error);
    }

    // ── Frequencies ───────────────────────────────────────────────────────────

    /// <summary>Blank and "weekly" mean every week: valid, with a null stored value.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("weekly")]
    [InlineData("Weekly")]
    [InlineData(" WEEKLY ")]
    public void ParseFrequency_WeeklyForms_AreNull(string? text)
    {
        var result = ScheduleText.ParseFrequency(text);

        Assert.True(result.IsValid, result.Error);
        Assert.Null(result.Value);
    }

    /// <summary>The odd and even words, with or without "weeks", any case and spacing.</summary>
    [Theory]
    [InlineData("odd", "odd")]
    [InlineData("odd weeks", "odd")]
    [InlineData("Odd Weeks", "odd")]
    [InlineData("  ODD   weeks ", "odd")]
    [InlineData("even", "even")]
    [InlineData("Even", "even")]
    [InlineData("even weeks", "even")]
    [InlineData("EVEN WEEKS", "even")]
    public void ParseFrequency_OddEvenWords_GiveStoredWord(string text, string expected)
    {
        var result = ScheduleText.ParseFrequency(text);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(expected, result.Value);
    }

    /// <summary>Anything else that is a valid week list is stored as a sorted week list.</summary>
    [Theory]
    [InlineData("1,6,7", "1,6,7")]
    [InlineData("7, 1 ,6", "1,6,7")]
    [InlineData("3", "3")]
    public void ParseFrequency_WeekList_IsSorted(string text, string expected)
    {
        var result = ScheduleText.ParseFrequency(text);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(expected, result.Value);
    }

    /// <summary>Text that is clearly a week list gets the week-list message when it is wrong.</summary>
    [Theory]
    [InlineData("0", "Each week must be a whole number ≥ 1")]
    [InlineData("1, 1", "Duplicate week numbers")]
    [InlineData(",", "Enter week numbers, e.g. 1,3,5")]
    [InlineData("1 2", "Each week must be a whole number ≥ 1")]
    public void ParseFrequency_BadWeekList_UsesWeekListMessage(string text, string expectedError)
    {
        var result = ScheduleText.ParseFrequency(text);

        Assert.False(result.IsValid);
        Assert.Equal(expectedError, result.Error);
    }

    /// <summary>Text that is neither a word nor a week list names the accepted forms.</summary>
    [Theory]
    [InlineData("fortnightly")]
    [InlineData("odd week")]
    [InlineData("1,3,x")]
    [InlineData("-1")]
    public void ParseFrequency_UnknownText_NamesAcceptedForms(string text)
    {
        var result = ScheduleText.ParseFrequency(text);

        Assert.False(result.IsValid);
        Assert.Equal(
            $"\"{text}\" isn't a frequency. Leave it blank for weekly, or use odd, even, or week numbers like 1,6,7.",
            result.Error);
    }
}
