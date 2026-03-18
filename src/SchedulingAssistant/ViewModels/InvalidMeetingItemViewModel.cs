using CommunityToolkit.Mvvm.ComponentModel;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.ViewModels;

/// <summary>
/// Represents a section whose schedule contains one or more meetings that are
/// incompatible with the academic year's legal block-length / start-time matrix.
/// Produced by the migration utility's meeting-validation scan and bound directly
/// in the Migration flyout's item list.
/// </summary>
public partial class InvalidMeetingItemViewModel : ObservableObject
{
    // ── Source data ───────────────────────────────────────────────────────────

    /// <summary>The section that contains the offending meetings.</summary>
    public Section Section { get; }

    /// <summary>
    /// The subset of <see cref="Section.Schedule"/> entries that failed the
    /// legal block-length / start-time check.
    /// </summary>
    public List<SectionDaySchedule> BadMeetings { get; }

    // ── Display labels ────────────────────────────────────────────────────────

    /// <summary>Human-readable section identifier, e.g. "FLOW 101 AB1  (Fall 2024)".</summary>
    public string SectionLabel { get; }

    /// <summary>
    /// Summary of the bad meetings for display, e.g.
    /// "Mon 8:00 AM – 9:20 AM, Wed 8:00 AM – 9:20 AM".
    /// </summary>
    public string BadMeetingsLabel { get; }

    // ── Action ────────────────────────────────────────────────────────────────

    /// <summary>All available actions, for binding to the ComboBox ItemsSource.</summary>
    public static IReadOnlyList<ActionChoice> AllActions { get; } =
        [ActionChoice.DiscardBadMeetings, ActionChoice.ReviseTime, ActionChoice.DiscardSection];

    /// <summary>Remediation options shown in the per-row ComboBox.</summary>
    public enum ActionChoice
    {
        /// <summary>Remove only the bad meeting entries; keep the section.</summary>
        DiscardBadMeetings,

        /// <summary>Replace all bad meetings with the user-chosen block length and start time.</summary>
        ReviseTime,

        /// <summary>Delete the entire section from the database.</summary>
        DiscardSection
    }

    /// <summary>
    /// Remediation action chosen by the user.
    /// Changing this also refreshes <see cref="ShowTimePickers"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTimePickers))]
    private ActionChoice _selectedAction = ActionChoice.DiscardBadMeetings;

    /// <summary>True when the time-picker controls should be visible in the UI.</summary>
    public bool ShowTimePickers => SelectedAction == ActionChoice.ReviseTime;

    // ── Time pickers (visible when SelectedAction == ReviseTime) ─────────────

    /// <summary>
    /// Block lengths (in hours) available for the section's academic year.
    /// Drives the block-length ComboBox.
    /// </summary>
    public IReadOnlyList<double> LegalBlockLengths { get; }

    /// <summary>
    /// Selected block length (hours) for the revised meeting time.
    /// Changing this refreshes <see cref="LegalStartTimesForBlock"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LegalStartTimesForBlock))]
    private double? _selectedBlockLength;

    /// <summary>
    /// Valid start times (minutes from midnight) for <see cref="SelectedBlockLength"/>.
    /// Drives the start-time ComboBox.
    /// </summary>
    public IReadOnlyList<int> LegalStartTimesForBlock
    {
        get
        {
            if (_selectedBlockLength is null) return [];
            var entry = _allLegalTimes
                .FirstOrDefault(lt => Math.Abs(lt.BlockLength - _selectedBlockLength.Value) < 0.01);
            return entry?.StartTimes ?? [];
        }
    }

    /// <summary>
    /// Selected start time (minutes from midnight) for the revised meeting.
    /// </summary>
    [ObservableProperty]
    private int? _selectedStartTime;

    // ── Internals ─────────────────────────────────────────────────────────────

    private readonly List<LegalStartTime> _allLegalTimes;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises the item view model.
    /// </summary>
    /// <param name="section">The section with incompatible meetings.</param>
    /// <param name="badMeetings">The specific meetings that failed the check.</param>
    /// <param name="legalTimes">Legal start-time matrix for the section's academic year.</param>
    /// <param name="sectionLabel">
    ///   Human-readable label shown in the list, e.g. "FLOW 101 AB1  (Fall 2024)".
    /// </param>
    public InvalidMeetingItemViewModel(
        Section section,
        List<SectionDaySchedule> badMeetings,
        List<LegalStartTime> legalTimes,
        string sectionLabel)
    {
        Section        = section;
        BadMeetings    = badMeetings;
        SectionLabel   = sectionLabel;
        _allLegalTimes = legalTimes;
        LegalBlockLengths = legalTimes.Select(lt => lt.BlockLength).ToList();

        BadMeetingsLabel = string.Join(", ", badMeetings.Select(m =>
        {
            var day = m.Day switch
            {
                1 => "Mon", 2 => "Tue", 3 => "Wed",
                4 => "Thu", 5 => "Fri", 6 => "Sat",
                _ => "?"
            };
            return $"{day} {MinutesToTime(m.StartMinutes)}–{MinutesToTime(m.StartMinutes + m.DurationMinutes)}";
        }));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a minutes-from-midnight value to a 12-hour clock string, e.g. "8:30 AM".
    /// </summary>
    /// <param name="minutes">Minutes from midnight.</param>
    /// <returns>Formatted time string.</returns>
    private static string MinutesToTime(int minutes)
    {
        var h      = minutes / 60;
        var m      = minutes % 60;
        var suffix = h < 12 ? "AM" : "PM";
        if (h == 0)      h = 12;
        else if (h > 12) h -= 12;
        return $"{h}:{m:D2} {suffix}";
    }
}
