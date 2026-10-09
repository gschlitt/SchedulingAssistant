using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading.Tasks;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// View-model-level tests for the meeting editor's Start / Length suggestion lists
/// (<see cref="SectionMeetingViewModel.AvailableStartTimeStrings"/> and
/// <see cref="SectionMeetingViewModel.AvailableBlockLengthStrings"/>) and the preferred-length
/// auto-fill.
///
/// <para>
/// The rule under test: <b>a suggestion list is rebuilt only when its own dropdown opens
/// (the <c>RefreshStartTimesCommand</c> / <c>RefreshBlockLengthsCommand</c> commands, bound to
/// <c>DropDownOpeningCommandBehavior</c>), plus once at construction. Committing a start time or a
/// block length never mutates either list.</b>
/// </para>
///
/// <para>
/// Regression context: spec item 22 (BugSnag <see cref="System.ArgumentOutOfRangeException"/> in
/// AutoCompleteBox when a bound list is rebuilt mid-pick) and the Avalonia 12.1.3 "lost selection"
/// regression, where clearing a sibling AutoCompleteBox's list during a commit made that box fall
/// back to its last typed text (empty after a mouse pick) and blank itself through its two-way
/// <c>Text</c> binding. No UI is involved here, so these tests pin the view-model half of the
/// contract: commits must not touch the lists, and the refresh commands must produce the right
/// contents without needless change notifications.
/// </para>
/// </summary>
public class MeetingSuggestionListTests
{
    // 1.5 h may start at 0830 or 1000; 3 h may start at 0830 or 1300.
    // So 1.5 h is legal at 0830 but NOT at 1300, and 3 h is legal at 0830 and 1300 but NOT at 1000.
    private static List<LegalStartTime> LegalTimes() => new()
    {
        new LegalStartTime { BlockLength = 1.5, StartTimes = new List<int> { 510, 600 } },
        new LegalStartTime { BlockLength = 3.0, StartTimes = new List<int> { 510, 780 } },
    };

    /// <summary>Formats a block length in hours exactly as the view model does (Hours unit).</summary>
    /// <param name="hours">Block length in hours.</param>
    /// <returns>The display string, e.g. "1.5" or "3".</returns>
    private static string Fmt(double hours) =>
        BlockLengthFormatter.FormatBlockLength(hours, BlockLengthUnit.Hours);

    /// <summary>Builds a meeting view model over <see cref="LegalTimes"/>.</summary>
    /// <param name="preferred">Preferred block length in hours, or null for none.</param>
    /// <param name="existing">An existing schedule entry to load, or null for a new (blank) meeting.</param>
    private static SectionMeetingViewModel MakeMeeting(double? preferred, SectionDaySchedule? existing) =>
        new(LegalTimes(),
            includeSaturday: false,
            includeSunday:   false,
            meetingTypes:    new List<SchedulingEnvironmentValue>(),
            rooms:           new List<Room>(),
            roomTypeOptions: new List<RoomTypeOption> { new(null, "(none)") },
            existing:        existing,
            defaultBlockLength: preferred,
            unit:            BlockLengthUnit.Hours);

    /// <summary>Builds a new (blank) meeting view model with the given preferred block length.</summary>
    /// <param name="preferred">Preferred block length in hours, or null for none.</param>
    private static SectionMeetingViewModel MakeNewMeeting(double? preferred) =>
        MakeMeeting(preferred, existing: null);

    /// <summary>
    /// Counts <see cref="INotifyCollectionChanged.CollectionChanged"/> notifications raised by a
    /// meeting's two suggestion lists, so a test can assert that an operation did (or did not)
    /// touch them. Any notification at all — Reset, Add, Remove — counts, because the
    /// AutoCompleteBox rebuilds its internal view on every one of them.
    /// </summary>
    private sealed class ListChangeCounter
    {
        /// <summary>Notifications seen on <c>AvailableStartTimeStrings</c> since the last reset.</summary>
        public int Start { get; private set; }

        /// <summary>Notifications seen on <c>AvailableBlockLengthStrings</c> since the last reset.</summary>
        public int Length { get; private set; }

        /// <summary>Starts counting changes on both lists of <paramref name="vm"/>.</summary>
        /// <param name="vm">The meeting whose lists to watch.</param>
        public ListChangeCounter(SectionMeetingViewModel vm)
        {
            vm.AvailableStartTimeStrings.CollectionChanged += (_, _) => Start++;
            vm.AvailableBlockLengthStrings.CollectionChanged += (_, _) => Length++;
        }

        /// <summary>Zeroes both counters.</summary>
        public void Reset()
        {
            Start = 0;
            Length = 0;
        }
    }

    /// <summary>
    /// Picking a start time where the preferred length is legal should auto-fill the length,
    /// both the committed value and the text shown in the Length box.
    /// </summary>
    [Fact]
    public void AutoFill_AfterLegalStartTime_FillsPreferredLength()
    {
        var vm = MakeNewMeeting(preferred: 1.5);

        vm.StartTimeText = "0830"; // what a dropdown pick writes via the two-way binding

        Assert.Equal(510, vm.SelectedStartTime);
        Assert.Equal(1.5, vm.SelectedBlockLength);
        Assert.Equal(Fmt(1.5), vm.BlockLengthText);
    }

    /// <summary>
    /// Spec item 22 crash path: after auto-fill, revising the start time to one where the preferred
    /// length is not legal must not rebuild the Start list mid-commit (the AutoCompleteBox being
    /// picked from crashes with ArgumentOutOfRangeException if its ItemsSource changes).
    /// </summary>
    [Fact]
    public void RevisingStartTime_DoesNotRebuildStartListDuringCommit()
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

    /// <summary>
    /// Committing a start time by picking a preset must leave both lists untouched — through the
    /// auto-fill path ("0830" fills the preferred 1.5 h) and through the clear path (revising to
    /// "1300" drops the 1.5 h that is no longer legal). This is the 12.1.3 regression guard: a
    /// rebuilt sibling list would blank the Length text.
    /// </summary>
    [Fact]
    public void CommittingStartTime_DoesNotChangeEitherList()
    {
        var vm = MakeNewMeeting(preferred: 1.5);
        var counter = new ListChangeCounter(vm);

        vm.StartTimeText = "0830"; // commits, auto-fills 1.5
        Assert.Equal(510, vm.SelectedStartTime);
        Assert.Equal(1.5, vm.SelectedBlockLength);

        vm.StartTimeText = "1300"; // commits, clears the now-illegal 1.5
        Assert.Equal(780, vm.SelectedStartTime);
        Assert.Null(vm.SelectedBlockLength);

        Assert.Equal(0, counter.Start);
        Assert.Equal(0, counter.Length);
    }

    /// <summary>
    /// Committing a block length by picking a preset must leave both lists untouched. This is the
    /// mirror image of the start-time case: a rebuilt Start list would blank the Start text.
    /// </summary>
    [Fact]
    public void CommittingBlockLength_DoesNotChangeEitherList()
    {
        var vm = MakeNewMeeting(preferred: null);
        vm.StartTimeText = "0830";
        var counter = new ListChangeCounter(vm);

        vm.BlockLengthText = Fmt(3.0); // a preset in the (full) Length list: auto-commits
        Assert.Equal(3.0, vm.SelectedBlockLength);

        Assert.Equal(0, counter.Start);
        Assert.Equal(0, counter.Length);
    }

    /// <summary>
    /// The LostFocus path: typing a value that is not a preset and then running the commit
    /// commands (as <c>LostFocusCommandBehavior</c> does) must leave both lists untouched. Custom
    /// values are used so that only the commands, not the exact-match auto-commit, do the commit.
    /// </summary>
    [Fact]
    public async Task CommitCommands_DoNotChangeEitherList()
    {
        var vm = MakeNewMeeting(preferred: null);
        var counter = new ListChangeCounter(vm);

        vm.StartTimeText = "0900"; // not a preset, so nothing auto-commits
        await vm.CommitStartTimeCommand.ExecuteAsync(null);
        Assert.Equal(540, vm.SelectedStartTime);

        vm.BlockLengthText = Fmt(2.0); // not a preset, so nothing auto-commits
        await vm.CommitBlockLengthCommand.ExecuteAsync(null);
        Assert.Equal(2.0, vm.SelectedBlockLength);

        Assert.Equal(0, counter.Start);
        Assert.Equal(0, counter.Length);
    }

    /// <summary>
    /// After a start time is committed, opening the Length dropdown (modelled by running
    /// <c>RefreshBlockLengthsCommand</c>) must offer exactly the lengths legal at that start time.
    /// Only 3 h is legal at 1300.
    /// </summary>
    [Fact]
    public void RefreshBlockLengths_AfterStartTimeCommit_OffersOnlyLegalLengths()
    {
        var vm = MakeNewMeeting(preferred: null);
        vm.StartTimeText = "1300";

        vm.RefreshBlockLengthsCommand.Execute(null);

        Assert.Equal(new[] { Fmt(3.0) }, vm.AvailableBlockLengthStrings);
    }

    /// <summary>
    /// After auto-fill commits the preferred 1.5 h, opening the Start dropdown (modelled by running
    /// <c>RefreshStartTimesCommand</c>) must offer exactly the start times where 1.5 h is legal:
    /// 0830 and 1000, not 1300.
    /// </summary>
    [Fact]
    public void RefreshStartTimes_AfterAutoFill_OffersOnlyTimesWherePreferredIsLegal()
    {
        var vm = MakeNewMeeting(preferred: 1.5);
        vm.StartTimeText = "0830"; // auto-fills 1.5

        vm.RefreshStartTimesCommand.Execute(null);

        Assert.Equal(new[] { "0830", "1000" }, vm.AvailableStartTimeStrings);
    }

    /// <summary>
    /// Revising the start time to one where the current length is not legal clears that length —
    /// both the committed value and the text — even though the Length list is not rebuilt.
    /// </summary>
    [Fact]
    public void RevisingStartTime_ClearsLengthNoLongerLegal()
    {
        var vm = MakeNewMeeting(preferred: 1.5);
        vm.StartTimeText = "0830"; // auto-fills 1.5
        Assert.Equal(1.5, vm.SelectedBlockLength);

        vm.StartTimeText = "1300"; // 1.5 h is not legal at 1300

        Assert.Null(vm.SelectedBlockLength);
        Assert.Equal("", vm.BlockLengthText);
    }

    /// <summary>
    /// Revising the start time to one where the current length is still legal keeps that length.
    /// 3 h is legal at both 0830 and 1300.
    /// </summary>
    [Fact]
    public void RevisingStartTime_KeepsLengthStillLegal()
    {
        var vm = MakeNewMeeting(preferred: null);
        vm.StartTimeText = "0830";
        vm.BlockLengthText = Fmt(3.0); // preset in the Length list: auto-commits
        Assert.Equal(3.0, vm.SelectedBlockLength);

        vm.StartTimeText = "1300"; // 3 h is legal at 1300

        Assert.Equal(780, vm.SelectedStartTime);
        Assert.Equal(3.0, vm.SelectedBlockLength);
        Assert.Equal(Fmt(3.0), vm.BlockLengthText);
    }

    /// <summary>
    /// A refresh whose computed list equals the collection's current contents must not touch the
    /// collection at all, so the AutoCompleteBox receives no change notification. Each command is
    /// first run when it genuinely changes its list (proving the setup is meaningful), then run a
    /// second time, which must raise nothing.
    /// </summary>
    [Fact]
    public void Refresh_WithUnchangedContents_DoesNotRaiseCollectionChanged()
    {
        var vm = MakeNewMeeting(preferred: null);
        vm.StartTimeText = "1300";
        vm.BlockLengthText = Fmt(3.0);   // 3 h is legal at 1300, so it is kept
        var counter = new ListChangeCounter(vm);

        // First refresh genuinely changes each list: Length narrows to [3], Start narrows to [0830, 1300].
        vm.RefreshBlockLengthsCommand.Execute(null);
        vm.RefreshStartTimesCommand.Execute(null);
        Assert.True(counter.Length > 0);
        Assert.True(counter.Start > 0);

        counter.Reset();
        vm.RefreshBlockLengthsCommand.Execute(null);
        vm.RefreshStartTimesCommand.Execute(null);

        Assert.Equal(0, counter.Length);
        Assert.Equal(0, counter.Start);
    }

    /// <summary>
    /// A meeting loaded from an existing schedule entry has its lists populated by the constructor
    /// (which runs before any UI binding) from the loaded values: Start times where the loaded
    /// 1.5 h length is legal, and the lengths legal at the loaded 0830 start.
    /// </summary>
    [Fact]
    public void ExistingMeeting_PopulatesListsForLoadedValues()
    {
        var existing = new SectionDaySchedule { Day = 1, StartMinutes = 510, DurationMinutes = 90 };

        var vm = MakeMeeting(preferred: null, existing);

        Assert.Equal(new[] { "0830", "1000" }, vm.AvailableStartTimeStrings);
        Assert.Equal(new[] { Fmt(1.5), Fmt(3.0) }, vm.AvailableBlockLengthStrings);
    }
}
