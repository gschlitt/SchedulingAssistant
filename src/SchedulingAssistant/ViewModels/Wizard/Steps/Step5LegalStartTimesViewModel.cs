using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
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

    /// <summary>
    /// The active display unit. Set by the parent VM when the user toggles the unit.
    /// Changing this raises <see cref="Label"/> for binding refresh.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private BlockLengthUnit _blockLengthUnit;

    /// <summary>Human-readable label shown in the card header, reflecting the active unit (e.g. "2 hours" or "120 min").</summary>
    public string Label => BlockLengthFormatter.LabelFor(BlockLengthHours, BlockLengthUnit);

    /// <summary>Editable list of start times for this block length.</summary>
    public ObservableCollection<WizardStartTimeEntry> StartTimes { get; } = [];

    /// <summary>Bound to the "add time" text box. Cleared after each successful add.</summary>
    [ObservableProperty] private string _newTimeInput = string.Empty;

    /// <summary>Validation error shown beneath the add-time row; null when no error is present.</summary>
    [ObservableProperty] private string? _addTimeError;

    /// <param name="blockLengthHours">Block length in hours.</param>
    /// <param name="unit">The current display unit.</param>
    /// <param name="initialTimes">Initial HHMM strings to populate the list.</param>
    public WizardBlockLengthEntry(double blockLengthHours, BlockLengthUnit unit, IEnumerable<string> initialTimes)
    {
        BlockLengthHours = blockLengthHours;
        _blockLengthUnit = unit;
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
            AddTimeError = $"A {BlockLengthFormatter.LabelFor(BlockLengthHours, BlockLengthUnit)} block starting then would end after 2200.";
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
    public override string StepTitle => "Block Length, Start Times and Schedule Days";

    /// <summary>The editable list of block lengths and their start times.</summary>
    public ObservableCollection<WizardBlockLengthEntry> BlockLengths { get; } = [];

    /// <summary>
    /// Input field for the new block length to add.
    /// In hours mode: decimal hours (e.g. "1.5"). In minutes mode: whole minutes (e.g. "90").
    /// </summary>
    [ObservableProperty] private string _newBlockLengthInput = string.Empty;

    /// <summary>Whether Saturday is available as a scheduling day. Saved to AppSettings on finish.</summary>
    [ObservableProperty] private bool _includeSaturday;

    /// <summary>Whether Sunday is available as a scheduling day. Saved to AppSettings on finish.</summary>
    [ObservableProperty] private bool _includeSunday;

    // ── Block length unit ─────────────────────────────────────────────────────

    private BlockLengthUnit _blockLengthUnit;

    /// <summary>
    /// Controls whether block lengths are displayed and entered in hours or minutes.
    /// Writes through to AppSettings and refreshes all card labels.
    /// </summary>
    public BlockLengthUnit BlockLengthUnit
    {
        get => _blockLengthUnit;
        set
        {
            if (_blockLengthUnit == value) return;
            _blockLengthUnit = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsHoursUnit));
            OnPropertyChanged(nameof(IsMinutesUnit));
            OnPropertyChanged(nameof(NewBlockLengthWatermark));
            OnPropertyChanged(nameof(NewBlockLengthLabel));

            // Propagate new unit to each card so their labels update.
            foreach (var entry in BlockLengths)
                entry.BlockLengthUnit = value;

            AppSettings.Current.BlockLengthUnit = value;
            AppSettings.Current.Save();
        }
    }

    /// <summary>True when the unit is Hours; used by RadioButton IsChecked binding.</summary>
    public bool IsHoursUnit
    {
        get => _blockLengthUnit == BlockLengthUnit.Hours;
        set { if (value) BlockLengthUnit = BlockLengthUnit.Hours; }
    }

    /// <summary>True when the unit is Minutes; used by RadioButton IsChecked binding.</summary>
    public bool IsMinutesUnit
    {
        get => _blockLengthUnit == BlockLengthUnit.Minutes;
        set { if (value) BlockLengthUnit = BlockLengthUnit.Minutes; }
    }

    /// <summary>Watermark shown in the "Add block length" input field.</summary>
    public string NewBlockLengthWatermark => BlockLengthFormatter.BlockLengthWatermark(_blockLengthUnit);

    /// <summary>Label shown beside the "Add block length" field.</summary>
    public string NewBlockLengthLabel => BlockLengthFormatter.BlockLengthInputLabel(_blockLengthUnit);

    public Step5LegalStartTimesViewModel()
    {
        _blockLengthUnit = AppSettings.Current.BlockLengthUnit;

        // Pre-populate from AppDefaults. The user can edit, delete, or add entries freely.
        foreach (var (hours, startMinutes) in AppDefaults.LegalStartTimes)
        {
            var hhmm = startMinutes.Select(AppDefaults.MinutesToHhmm);
            BlockLengths.Add(new WizardBlockLengthEntry(hours, _blockLengthUnit, hhmm));
        }
    }

    /// <summary>
    /// Adds a new block length card using the value in <see cref="NewBlockLengthInput"/>.
    /// In hours mode: parses decimal hours. In minutes mode: parses whole minutes.
    /// Ignores non-positive or duplicate values. Clears the input field on success.
    /// </summary>
    [RelayCommand]
    private void AddBlockLength()
    {
        double hours;

        if (_blockLengthUnit == BlockLengthUnit.Minutes)
        {
            // Whole minutes only (e.g. "90")
            if (!int.TryParse(NewBlockLengthInput.Trim(), out int mins) || mins <= 0)
                return;
            hours = mins / 60.0;
        }
        else
        {
            // Decimal hours (e.g. "1.5") — accept both "." and "," as separators
            var normalized = NewBlockLengthInput.Trim().Replace(',', '.');
            if (!double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out hours)
                || hours <= 0)
                return;
        }

        // Prevent duplicates (within 0.01h tolerance)
        if (BlockLengths.Any(b => Math.Abs(b.BlockLengthHours - hours) < 0.01))
            return;

        BlockLengths.Add(new WizardBlockLengthEntry(hours, _blockLengthUnit, []));
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
