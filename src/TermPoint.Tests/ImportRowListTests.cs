using TermPoint.Services.SectionImport;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="ImportRowList"/>, the one formatter every grouped warning uses for the rows
/// it affects, and the small wording helpers in <see cref="ImportText"/>.
/// </summary>
public class ImportRowListTests
{
    /// <summary>The list reads naturally from one row up to many.</summary>
    [Theory]
    [InlineData(new[] { 4 }, "row 4")]
    [InlineData(new[] { 4, 9 }, "rows 4 and 9")]
    [InlineData(new[] { 4, 9, 12 }, "rows 4, 9 and 12")]
    [InlineData(new[] { 4, 9, 12, 15, 18 }, "rows 4, 9, 12, 15 and 18")]
    [InlineData(new[] { 4, 9, 12, 15, 18, 25 }, "rows 4, 9, 12, 15, 18 and 1 more")]
    public void Format_ReadsNaturally(int[] rows, string expected) =>
        Assert.Equal(expected, ImportRowList.Format(rows));

    /// <summary>Thirty rows name the first five and count the other twenty-five.</summary>
    [Fact]
    public void Format_ThirtyRows_NamesFiveAndCountsTheRest() =>
        Assert.Equal("rows 4, 9, 12, 15, 18 and 25 more",
            ImportRowList.Format(new[] { 4, 9, 12, 15, 18 }.Concat(Enumerable.Range(100, 25)).ToArray()));

    /// <summary>No rows is an empty string (a file-wide warning has none).</summary>
    [Fact]
    public void Format_NoRows_IsEmpty() => Assert.Equal("", ImportRowList.Format(Array.Empty<int>()));

    /// <summary>Meeting lengths are written in hours when they are whole or half hours, otherwise in minutes.</summary>
    [Theory]
    [InlineData(60, "1-hour")]
    [InlineData(90, "1.5-hour")]
    [InlineData(120, "2-hour")]
    [InlineData(180, "3-hour")]
    [InlineData(50, "50-minute")]
    [InlineData(75, "75-minute")]
    public void LengthAdjective_HoursOrMinutes(int minutes, string expected) =>
        Assert.Equal(expected, ImportText.LengthAdjective(minutes));

    /// <summary>Times use the four-digit military form.</summary>
    [Theory]
    [InlineData(450, "0730")]
    [InlineData(1065, "1745")]
    public void Time_IsFourDigits(int minutes, string expected) => Assert.Equal(expected, ImportText.Time(minutes));
}
