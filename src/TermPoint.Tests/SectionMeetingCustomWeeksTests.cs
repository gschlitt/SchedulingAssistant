using TermPoint.Models;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Pins the meeting editor's Custom frequency behaviour: the three validation messages word for
/// word and the stored value. The editor shares its parsing with the section importer
/// (<c>ScheduleText.ParseWeekList</c>), so these tests guard against that sharing changing what
/// users see.
/// </summary>
public class SectionMeetingCustomWeeksTests
{
    /// <summary>Creates an empty meeting row (no legal start times, rooms or meeting types) for editing tests.</summary>
    private static SectionMeetingViewModel NewMeeting() => new(
        legalStartTimes: new List<LegalStartTime>(),
        includeSaturday: false,
        includeSunday: false,
        meetingTypes: new List<SchedulingEnvironmentValue>(),
        rooms: new List<Room>(),
        roomTypeOptions: new List<RoomTypeOption> { new(null, "(none)") });

    /// <summary>Selects Custom and types <paramref name="weeks"/> into the Weeks field.</summary>
    private static SectionMeetingViewModel CustomWith(string weeks)
    {
        var vm = NewMeeting();
        vm.SelectedFrequencyOption = "Custom";
        vm.CustomWeeksText = weeks;
        return vm;
    }

    /// <summary>The stored frequency as the editor would save it (via the Room Browser spec, which needs only a block length).</summary>
    private static string? StoredFrequency(SectionMeetingViewModel vm)
    {
        vm.SelectedBlockLength = 1.5;
        return vm.ToMeetingSpec(0)!.Frequency;
    }

    /// <summary>Choosing Custom with nothing typed shows the "Enter week numbers" prompt.</summary>
    [Fact]
    public void Custom_Blank_ShowsEnterMessage()
    {
        var vm = NewMeeting();
        vm.SelectedFrequencyOption = "Custom";

        Assert.Equal("Enter week numbers, e.g. 1,3,5", vm.CustomWeeksError);
        Assert.Null(StoredFrequency(vm));
    }

    /// <summary>A non-number, zero or negative week shows the "whole number" message.</summary>
    [Theory]
    [InlineData("a")]
    [InlineData("1,x")]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("1.5")]
    public void Custom_NotAWholeNumber_ShowsWholeNumberMessage(string weeks)
    {
        var vm = CustomWith(weeks);

        Assert.Equal("Each week must be a whole number ≥ 1", vm.CustomWeeksError);
        Assert.Null(StoredFrequency(vm));
    }

    /// <summary>A repeated week shows the "Duplicate" message.</summary>
    [Fact]
    public void Custom_RepeatedWeek_ShowsDuplicateMessage()
    {
        var vm = CustomWith("1, 3, 1");

        Assert.Equal("Duplicate week numbers", vm.CustomWeeksError);
        Assert.Null(StoredFrequency(vm));
    }

    /// <summary>A valid list clears the error and is stored sorted with no spaces.</summary>
    [Fact]
    public void Custom_ValidList_HasNoErrorAndIsStoredSorted()
    {
        var vm = CustomWith("6, 1,7");

        Assert.Null(vm.CustomWeeksError);
        Assert.Equal("1,6,7", StoredFrequency(vm));
    }

    /// <summary>Fixing a bad entry clears the message, and leaving Custom clears it too.</summary>
    [Fact]
    public void Custom_ErrorClearsWhenFixedOrWhenCustomIsDeselected()
    {
        var vm = CustomWith("x");
        Assert.NotNull(vm.CustomWeeksError);

        vm.CustomWeeksText = "2,4";
        Assert.Null(vm.CustomWeeksError);

        vm.CustomWeeksText = "x";
        vm.SelectedFrequencyOption = "Odd weeks";
        Assert.Null(vm.CustomWeeksError);
    }

    /// <summary>No validation message appears for text typed while Custom is not selected.</summary>
    [Fact]
    public void NotCustom_NeverShowsAnError()
    {
        var vm = NewMeeting();
        vm.CustomWeeksText = "x";

        Assert.Null(vm.CustomWeeksError);
    }
}
