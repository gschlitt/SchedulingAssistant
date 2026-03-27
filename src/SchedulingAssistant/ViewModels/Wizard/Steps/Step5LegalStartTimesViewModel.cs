using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.ViewModels.Management;
using System.Collections.ObjectModel;
using System.Globalization;

namespace SchedulingAssistant.ViewModels.Wizard.Steps;

/// <summary>
/// An editable start time entry within a block length row.
/// Stored in HHMM military format (e.g. "0800" for 8:00 AM).
/// </summary>
public partial class WizardStartTimeEntry : ViewModelBase
{
    /// <summary>Start time in HHMM military format (e.g. "0800").</summary>
    [ObservableProperty] private string _hhmm;

    /// <param name="hhmm">Start time in HHMM military format.</param>
    public WizardStartTimeEntry(string hhmm) => Hhmm = hhmm;

    /// <summary>
    /// Converts the HHMM string to minutes from midnight.
    /// Returns -1 if the value is not parseable or out of range.
    /// </summary>
    public int ToMinutes()
    {
        if (int.TryParse(Hhmm.Trim(), out var n) && n is >= 0 and <= 2359)
            return (n / 100) * 60 + (n % 100);
        return -1;
    }
}

/// <summary>
/// One block-length entry (duration in hours plus a list of editable start times).
/// Used by the Block Lengths &amp; Start Times wizard step and can be embedded in Settings views.
/// </summary>
public partial class WizardBlockLengthEntry : ViewModelBase
{
    /// <summary>Block length in hours (e.g. 1.5, 2.0, 3.0). Stored as-is in the DB.</summary>
    public double BlockLengthHours { get; }

    /// <summary>Human-readable label shown in the card header (e.g. "2 hours").</summary>
    public string Label { get; }

    /// <summary>Editable list of start times for this block length.</summary>
    public ObservableCollection<WizardStartTimeEntry> StartTimes { get; } = [];

    /// <summary>Bound to the "add time" text box. Cleared after each successful add.</summary>
    [ObservableProperty] private string _newTimeInput = string.Empty;

    /// <summary>Validation error shown beneath the add-time row; null when no error is present.</summary>
    [ObservableProperty] private string? _addTimeError;

    /// <param name="blockLengthHours">Block length in hours.</param>
    /// <param name="label">Display label.</param>
    /// <param name="initialTimes">Initial HHMM strings to populate the list.</param>
    public WizardBlockLengthEntry(double blockLengthHours, string label, IEnumerable<string> initialTimes)
    {
        BlockLengthHours = blockLengthHours;
        Label            = label;
        foreach (var t in initialTimes)
            StartTimes.Add(new WizardStartTimeEntry(t));
    }

    /// <summary>
    /// Adds the current <see cref="NewTimeInput"/> as a new start time entry.
    /// Validates that the value is a 4-digit military time (0000–2359) before adding.
    /// Sets <see cref="AddTimeError"/> on failure; clears the input field on success.
    /// </summary>
    [RelayCommand]
    private void AddTime()
    {
        var trimmed = NewTimeInput.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;

        if (!IsValidMilitaryTime(trimmed, out var errorMsg))
        {
            AddTimeError = errorMsg;
            return;
        }

        int minutes = (int.Parse(trimmed) / 100) * 60 + (int.Parse(trimmed) % 100);
        if (minutes < SectionMeetingViewModel.MinStartMinutes)
        {
            AddTimeError = "Start times cannot be earlier than 0730.";
            return;
        }
        int endMinutes = minutes + (int)Math.Round(BlockLengthHours * 60);
        if (endMinutes > SectionMeetingViewModel.MaxEndMinutes)
        {
            AddTimeError = $"A {BlockLengthHours}h block starting then would end after 2200.";
            return;
        }

        // Reject duplicates within this block
        if (StartTimes.Any(e => e.Hhmm == trimmed))
        {
            AddTimeError = $"{trimmed} is already in this block.";
            return;
        }

        StartTimes.Add(new WizardStartTimeEntry(trimmed));
        NewTimeInput  = string.Empty;
        AddTimeError  = null;
    }

    /// <summary>
    /// Returns true when <paramref name="value"/> is a valid 4-digit military time (0000–2359).
    /// Sets <paramref name="error"/> to a user-facing message on failure.
    /// </summary>
    /// <param name="value">Raw input string to validate.</param>
    /// <param name="error">Error message if validation fails; null on success.</param>
    /// <returns>True when valid; false otherwise.</returns>
    public static bool IsValidMilitaryTime(string value, out string? error)
    {
        if (value.Length != 4 || !int.TryParse(value, out var n))
        {
            error = "Enter exactly 4 digits (e.g. 0800).";
            return false;
        }

        var hh = n / 100;
        var mm = n % 100;

        if (hh > 23 || mm > 59)
        {
            error = $"{value} is not a valid time — hours must be 00–23, minutes 00–59.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>Removes the given start time entry from the list.</summary>
    /// <param name="entry">The entry to remove.</param>
    [RelayCommand]
    private void RemoveTime(WizardStartTimeEntry entry) => StartTimes.Remove(entry);

    /// <summary>
    /// Returns all valid start times as minutes from midnight,
    /// filtering out any unparseable or out-of-range entries.
    /// </summary>
    public List<int> GetStartMinutes() =>
        StartTimes.Select(e => e.ToMinutes()).Where(m => m >= 0).ToList();

    /// <summary>
    /// Builds a human-readable label from a block length in hours.
    /// Whole numbers render as "N hours"; fractions as "N.N hours".
    /// </summary>
    /// <param name="hours">Block length in hours.</param>
    public static string LabelFor(double hours) =>
        hours == Math.Floor(hours) ? $"{(int)hours} hours" : $"{hours} hours";
}

/// <summary>
/// View model for the Block Lengths &amp; Start Times editor.
/// Used both in the startup wizard (Step 5, manual path) and in the Settings flyout.
/// Pre-seeded with a 2-hour and a 3-hour block example; the user can modify, delete,
/// or add block lengths before proceeding. Data is applied to the DB after the first
/// academic year is created.
/// </summary>
public partial class Step5LegalStartTimesViewModel : WizardStepViewModel
{
    public override string StepTitle => "Block Lengths & Start Times";

    /// <summary>The editable list of block lengths and their start times.</summary>
    public ObservableCollection<WizardBlockLengthEntry> BlockLengths { get; } = [];

    /// <summary>
    /// Input field for the new block length to add, in hours (e.g. "1.5").
    /// Accepts a decimal number; leading zeros and commas are tolerated.
    /// </summary>
    [ObservableProperty] private string _newBlockLengthInput = string.Empty;

    /// <summary>Whether Saturday is available as a scheduling day. Saved to AppSettings on finish.</summary>
    [ObservableProperty] private bool _includeSaturday;

    /// <summary>Whether Sunday is available as a scheduling day. Saved to AppSettings on finish.</summary>
    [ObservableProperty] private bool _includeSunday;

    public Step5LegalStartTimesViewModel()
    {
        // Example block lengths. Times are in HHMM military format.
        // The user can delete these and add their own before proceeding.
        BlockLengths.Add(new WizardBlockLengthEntry(
            2.0, "2 hours",
            ["0800", "1000", "1200", "1400", "1600", "1700", "1800", "1900"]
        ));
        BlockLengths.Add(new WizardBlockLengthEntry(
            3.0, "3 hours",
            ["0900", "1200", "1500", "1800"]
        ));
    }

    /// <summary>
    /// Adds a new block length card using the value in <see cref="NewBlockLengthInput"/>.
    /// Ignores non-positive or duplicate values. Clears the input field on success.
    /// </summary>
    [RelayCommand]
    private void AddBlockLength()
    {
        // Accept both "1.5" and "1,5" as decimal separators
        var normalized = NewBlockLengthInput.Trim().Replace(',', '.');
        if (!double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var hours)
            || hours <= 0)
            return;

        // Prevent duplicates (within 0.01h tolerance)
        if (BlockLengths.Any(b => Math.Abs(b.BlockLengthHours - hours) < 0.01))
            return;

        BlockLengths.Add(new WizardBlockLengthEntry(hours, WizardBlockLengthEntry.LabelFor(hours), []));
        NewBlockLengthInput = string.Empty;
    }

    /// <summary>
    /// Removes the given block length card and all its start times.
    /// </summary>
    /// <param name="entry">The block length entry to remove.</param>
    [RelayCommand]
    private void RemoveBlockLength(WizardBlockLengthEntry entry) => BlockLengths.Remove(entry);

    /// <summary>
    /// Returns the configured block lengths and their start times (minutes from midnight),
    /// ready for DB insertion. Entries with no valid start times are omitted.
    /// Used by <see cref="StartupWizardViewModel"/> to seed the first academic year.
    /// </summary>
    public IReadOnlyList<(double BlockLengthHours, List<int> StartMinutes)> GetSeedData() =>
        BlockLengths
            .Select(b => (b.BlockLengthHours, b.GetStartMinutes()))
            .Where(x => x.Item2.Count > 0)
            .ToList();
}
