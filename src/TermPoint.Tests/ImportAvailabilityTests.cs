using TermPoint.Services.SectionImport;
using Xunit;

namespace TermPoint.Tests;

/// <summary>Tests of <see cref="ImportAvailability.WhyUnavailable"/>: each case, and the order the cases are checked in.</summary>
public sealed class ImportAvailabilityTests
{
    /// <summary>One writable session, one semester shown, no editor open: available (null).</summary>
    [Fact]
    public void Available_WhenWritableWithOneSemesterAndNoEditor()
    {
        Assert.Null(ImportAvailability.WhyUnavailable(isWriteEnabled: true, shownSemesterCount: 1, isEditingSection: false));
    }

    /// <summary>A read-only session says so.</summary>
    [Fact]
    public void ReadOnly_GivesTheReadOnlyReason()
    {
        Assert.Equal(
            "TermPoint is read-only right now, so sections can't be imported.",
            ImportAvailability.WhyUnavailable(false, 1, false));
    }

    /// <summary>No semester shown asks the user to open one.</summary>
    [Fact]
    public void NoSemester_GivesTheOpenASemesterReason()
    {
        Assert.Equal("Open a semester to import into it.", ImportAvailability.WhyUnavailable(true, 0, false));
    }

    /// <summary>Several semesters shown asks the user to show one.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void SeveralSemesters_GivesTheShowOneSemesterReason(int count)
    {
        Assert.Equal("Show one semester to import into it.", ImportAvailability.WhyUnavailable(true, count, false));
    }

    /// <summary>A section open for editing asks the user to finish first.</summary>
    [Fact]
    public void Editing_GivesTheFinishEditingReason()
    {
        Assert.Equal("Finish editing the section first.", ImportAvailability.WhyUnavailable(true, 1, true));
    }

    /// <summary>
    /// When several things apply, the first in the order read-only, no semester, several semesters, editing wins.
    /// </summary>
    [Theory]
    [InlineData(false, 0, true, ImportAvailability.ReadOnlyReason)]
    [InlineData(false, 3, true, ImportAvailability.ReadOnlyReason)]
    [InlineData(false, 1, true, ImportAvailability.ReadOnlyReason)]
    [InlineData(true, 0, true, ImportAvailability.NoSemesterReason)]
    [InlineData(true, 2, true, ImportAvailability.SeveralSemestersReason)]
    public void Order_ReadOnly_ThenNoSemester_ThenSeveral_ThenEditing(
        bool write, int count, bool editing, string expected)
    {
        Assert.Equal(expected, ImportAvailability.WhyUnavailable(write, count, editing));
    }
}
