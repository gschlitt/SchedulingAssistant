using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class LegalStartTimeEditViewModel : ViewModelBase
{
    [ObservableProperty] private double _blockLength;
    [ObservableProperty] private ObservableCollection<StartTimeRowViewModel> _startTimeRows = new();
    [ObservableProperty] private string _newStartTime = string.Empty;
    [ObservableProperty] private string? _validationError;

    public string Title => IsNew ? "Add Block Length" : "Edit Block Length";
    public bool IsNew { get; }

    private readonly LegalStartTime _entry;
    private readonly Action<LegalStartTime> _onSave;
    private readonly Action _onCancel;

    public LegalStartTimeEditViewModel(LegalStartTime entry, bool isNew, Action<LegalStartTime> onSave, Action onCancel)
    {
        _entry = entry;
        IsNew = isNew;
        _onSave = onSave;
        _onCancel = onCancel;

        BlockLength = entry.BlockLength;
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
    private void Save()
    {
        _entry.BlockLength = BlockLength;
        _entry.StartTimes = StartTimeRows.Select(r => r.Minutes).ToList();
        _onSave(_entry);
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
        Minutes = minutes;
        _onRemove = onRemove;
        int h = minutes / 60, m = minutes % 60;
        Label = $"{h:D2}:{m:D2}";
    }

    [RelayCommand]
    private void Remove() => _onRemove(this);
}
