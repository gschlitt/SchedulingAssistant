using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class LegalStartTimeEditViewModel : ViewModelBase
{
    /// <summary>Block length in hours — internal storage unit. Never exposed directly to the UI.</summary>
    private double _blockLengthHours;

    /// <summary>
    /// Block length as shown in the NumericUpDown, expressed in the current <see cref="_unit"/>.
    /// Hours mode: hours (e.g. 1.5). Minutes mode: whole minutes (e.g. 90).
    /// Setting this converts back to hours for internal storage.
    /// </summary>
    public double BlockLengthDisplay
    {
        get => BlockLengthFormatter.HoursToDisplay(_blockLengthHours, _unit);
        set
        {
            double newHours = BlockLengthFormatter.DisplayToHours(value, _unit);
            if (Math.Abs(_blockLengthHours - newHours) < 0.0001) return;
            _blockLengthHours = newHours;
            OnPropertyChanged();
        }
    }

    [ObservableProperty] private ObservableCollection<StartTimeRowViewModel> _startTimeRows = new();
    [ObservableProperty] private string _newStartTime = string.Empty;
    [ObservableProperty] private string? _validationError;

    public string Title => IsNew ? "Add Block Length" : "Edit Block Length";
    public bool IsNew { get; }

    /// <summary>Label for the block-length field, e.g. "Block Length (hours)" or "Block Length (minutes)".</summary>
    public string BlockLengthLabel => BlockLengthFormatter.BlockLengthInputLabel(_unit);

    /// <summary>NumericUpDown increment in display units.</summary>
    public decimal NumericIncrement => BlockLengthFormatter.NumericIncrement(_unit);

    /// <summary>NumericUpDown minimum in display units.</summary>
    public decimal NumericMinimum => BlockLengthFormatter.NumericMinimum(_unit);

    /// <summary>NumericUpDown maximum in display units.</summary>
    public decimal NumericMaximum => BlockLengthFormatter.NumericMaximum(_unit);

    /// <summary>NumericUpDown format string.</summary>
    public string NumericFormatString => BlockLengthFormatter.NumericFormatString(_unit);

    private readonly LegalStartTime _entry;
    private readonly BlockLengthUnit _unit;
    private readonly Func<LegalStartTime, Task> _onSave;
    private readonly Action _onCancel;

    /// <summary>
    /// Optional predicate that returns true when the given block length (in hours) is already
    /// taken by another entry. Only evaluated when <see cref="IsNew"/> is true.
    /// </summary>
    private readonly Func<double, bool>? _isDuplicateBlockLength;

    /// <param name="entry">The entry being edited or a blank entry for new additions.</param>
    /// <param name="isNew">True when adding a new block length; false when editing an existing one.</param>
    /// <param name="unit">Current block-length display unit from AppSettings.</param>
    /// <param name="onSave">Callback invoked with the updated entry on save.</param>
    /// <param name="onCancel">Callback invoked when the user cancels.</param>
    /// <param name="isDuplicateBlockLength">
    /// Optional. Called during save (add mode only) to check whether the entered block length
    /// conflicts with an existing entry. Return true to abort the save and display an error.
    /// </param>
    public LegalStartTimeEditViewModel(LegalStartTime entry, bool isNew, BlockLengthUnit unit,
        Func<LegalStartTime, Task> onSave, Action onCancel,
        Func<double, bool>? isDuplicateBlockLength = null)
    {
        _entry  = entry;
        IsNew   = isNew;
        _unit   = unit;
        _onSave = onSave;
        _onCancel = onCancel;
        _isDuplicateBlockLength = isDuplicateBlockLength;

        _blockLengthHours = entry.BlockLength;
        StartTimeRows = new ObservableCollection<StartTimeRowViewModel>(
            entry.StartTimes.Select(m => new StartTimeRowViewModel(m, Remove)));
    }

    private void Remove(StartTimeRowViewModel row) => StartTimeRows.Remove(row);

    [RelayCommand]
    private void AddStartTime()
    {
        ValidationError = null;
        var input = NewStartTime.Trim();
        if (!TryParseTime(input, out int minutes))
        {
            ValidationError = "Enter a time like 8:30 or 1430";
            return;
        }
        if (minutes < SectionMeetingViewModel.MinStartMinutes)
        {
            ValidationError = "Start times cannot be earlier than 0730.";
            return;
        }
        int endMinutes = minutes + (int)Math.Round(_blockLengthHours * 60);
        if (endMinutes > SectionMeetingViewModel.MaxEndMinutes)
        {
            ValidationError = $"A {BlockLengthFormatter.LabelFor(_blockLengthHours, _unit)} block starting then would end after 2200.";
            return;
        }
        if (StartTimeRows.Any(r => r.Minutes == minutes))
        {
            ValidationError = "That time is already in the list";
            return;
        }
        StartTimeRows.Add(new StartTimeRowViewModel(minutes, Remove));
        var sorted = StartTimeRows.OrderBy(r => r.Minutes).ToList();
        StartTimeRows = new ObservableCollection<StartTimeRowViewModel>(sorted);
        NewStartTime = string.Empty;
    }

    private static bool TryParseTime(string input, out int minutes)
    {
        minutes = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;

        // Try HHMM format e.g. "1430"
        if (input.Length == 4 && int.TryParse(input, out int hhmm))
        {
            int h = hhmm / 100, m = hhmm % 100;
            if (h is >= 0 and <= 23 && m is >= 0 and <= 59) { minutes = h * 60 + m; return true; }
        }

        // Try H:MM or HH:MM format
        if (input.Contains(':'))
        {
            var parts = input.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out int h) && int.TryParse(parts[1], out int m))
            {
                if (h is >= 0 and <= 23 && m is >= 0 and <= 59) { minutes = h * 60 + m; return true; }
            }
        }

        return false;
    }

    [RelayCommand]
    private async Task Save()
    {
        if (IsNew && _isDuplicateBlockLength?.Invoke(_blockLengthHours) == true)
        {
            ValidationError = $"A block length of {BlockLengthFormatter.LabelFor(_blockLengthHours, _unit)} already exists.";
            return;
        }

        ValidationError = null;
        _entry.BlockLength = _blockLengthHours;
        _entry.StartTimes  = StartTimeRows.Select(r => r.Minutes).ToList();
        await _onSave(_entry);
    }

    [RelayCommand]
    private void Cancel() => _onCancel();
}

public partial class StartTimeRowViewModel : ViewModelBase
{
    public int Minutes { get; }
    public string Label { get; }

    private readonly Action<StartTimeRowViewModel> _onRemove;

    public StartTimeRowViewModel(int minutes, Action<StartTimeRowViewModel> onRemove)
    {
        Minutes   = minutes;
        _onRemove = onRemove;
        Label     = $"{minutes / 60:D2}{minutes % 60:D2}";
    }

    [RelayCommand]
    private void Remove() => _onRemove(this);
}
