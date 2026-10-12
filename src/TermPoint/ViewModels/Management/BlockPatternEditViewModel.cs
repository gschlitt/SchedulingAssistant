using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Models;
using System.Collections.ObjectModel;

namespace TermPoint.ViewModels.Management;

public partial class BlockPatternEditViewModel : ViewModelBase
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string? _validationError;

    public ObservableCollection<DayCheckViewModel> Days { get; }

    private readonly int _slot;
    private readonly Action<BlockPattern> _onSave;
    private readonly Action _onCancel;

    /// <summary>
    /// The stored pattern being edited, or null when this slot is empty and a new pattern is being
    /// created. <see cref="Save"/> builds a brand-new <see cref="BlockPattern"/> rather than mutating
    /// this one, so it uses it to carry across the original's unknown JSON fields
    /// (<see cref="PersistedRecord.ExtensionData"/>, spec item 3); otherwise saving from an older
    /// app version would delete fields a newer version stored on the pattern.
    /// </summary>
    private readonly BlockPattern? _existing;

    public BlockPatternEditViewModel(
        int slot,
        BlockPattern? existing,
        bool includeSaturday,
        bool includeSunday,
        Action<BlockPattern> onSave,
        Action onCancel)
    {
        _slot = slot;
        _existing = existing;
        _onSave = onSave;
        _onCancel = onCancel;

        Name = existing?.Name ?? string.Empty;

        var dayList = new List<DayCheckViewModel>
        {
            new(1, "Mon", existing?.Days.Contains(1) ?? false),
            new(2, "Tue", existing?.Days.Contains(2) ?? false),
            new(3, "Wed", existing?.Days.Contains(3) ?? false),
            new(4, "Thu", existing?.Days.Contains(4) ?? false),
            new(5, "Fri", existing?.Days.Contains(5) ?? false),
        };
        if (includeSaturday)
            dayList.Add(new(6, "Sat", existing?.Days.Contains(6) ?? false));
        if (includeSunday)
            dayList.Add(new(7, "Sun", existing?.Days.Contains(7) ?? false));

        Days = new ObservableCollection<DayCheckViewModel>(dayList);
    }

    [RelayCommand]
    private void Save()
    {
        var trimmedName = Name.Trim();
        var selectedDays = Days.Where(d => d.IsChecked).Select(d => d.Day).ToList();

        if (string.IsNullOrEmpty(trimmedName))
        {
            ValidationError = "A name is required.";
            return;
        }
        if (selectedDays.Count == 0)
        {
            ValidationError = "Select at least one day.";
            return;
        }

        ValidationError = null;
        _onSave(new BlockPattern { Name = trimmedName, Days = selectedDays }.WithExtensionDataFrom(_existing));
    }

    [RelayCommand]
    private void Cancel() => _onCancel();
}
