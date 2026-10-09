using System.Collections.Generic;
using System.Collections.Specialized;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// PROBE (2026-10-09): view-model-level checks for the preferred-block-length auto-fill and the
/// suspected Start-list rebuild during a start-time commit. No UI is involved, so these separate
/// view-model logic from AutoCompleteBox / binding behaviour.
/// </summary>
public class PreferredBlockLengthProbeTests
{
    // 1.5 h may start at 0830 or 1000; 3 h may start at 0830 or 1300.
    // So 1.5 h is legal at 0830 but NOT at 1300.
    private static List<LegalStartTime> LegalTimes() => new()
    {
        new LegalStartTime { BlockLength = 1.5, StartTimes = new List<int> { 510, 600 } },
        new LegalStartTime { BlockLength = 3.0, StartTimes = new List<int> { 510, 780 } },
    };

    /// <summary>Builds a new (blank) meeting view model with the given preferred block length.</summary>
    /// <param name="preferred">Preferred block length in hours, or null for none.</param>
    private static SectionMeetingViewModel MakeNewMeeting(double? preferred) =>
        new(LegalTimes(),
            includeSaturday: false,
            includeSunday:   false,
            meetingTypes:    new List<SchedulingEnvironmentValue>(),
            rooms:           new List<Room>(),
            roomTypeOptions: new List<RoomTypeOption> { new(null, "(none)") },
            existing:        null,
            defaultBlockLength: preferred,
            unit:            BlockLengthUnit.Hours);

    /// <summary>
    /// Picking a start time where the preferred length is legal should auto-fill the length,
    /// both the committed value and the text shown in the Length box.
    /// </summary>
    [Fact]
    public void Probe_AutoFill_AfterLegalStartTime_FillsPreferredLength()
    {
        var vm = MakeNewMeeting(preferred: 1.5);

        vm.StartTimeText = "0830"; // what a dropdown pick writes via the two-way binding

        Assert.Equal(510, vm.SelectedStartTime);
        Assert.Equal(1.5, vm.SelectedBlockLength);
        Assert.Equal(BlockLengthFormatter.FormatBlockLength(1.5, BlockLengthUnit.Hours), vm.BlockLengthText);
    }

    /// <summary>
    /// Suspected crash path: after auto-fill, revising the start time to one where the preferred
    /// length is not legal must not rebuild the Start list mid-commit (the AutoCompleteBox being
    /// picked from crashes with ArgumentOutOfRangeException if its ItemsSource changes).
    /// </summary>
    [Fact(Skip = "Known bug — spec item 22 (BugSnag ArgumentOutOfRangeException). Unskip when fixed.")]
    public void Probe_RevisingStartTime_DoesNotRebuildStartListDuringCommit()
    {
        var vm = MakeNewMeeting(preferred: 1.5);
        vm.StartTimeText = "0830"; // auto-fills 1.5

        int startListChanges = 0;
        NotifyCollectionChangedEventHandler h = (_, _) => startListChanges++;
        vm.AvailableStartTimeStrings.CollectionChanged += h;

        vm.StartTimeText = "1300"; // 1.5 h is not legal at 1300

        vm.AvailableStartTimeStrings.CollectionChanged -= h;
        Assert.Equal(0, startListChanges);
    }
}
